import {ScreenRelayState} from './screen-relay.mjs';
import {FileRelayState} from './file-relay.mjs';
/** JARVIS private three-device relay. Only trusted device clients call these
 * endpoints. The same user-controlled Worker bearer token authenticates them.
 * This first-party prototype is NOT suitable for multi-user public deployment.
 *
 * Messages expire quickly and are never fed to AI; recipients apply their own
 * local command allowlists/permissions. No passwords, PINs or API keys allowed.
 */
const DEVICES = new Set(['phone','watch','computer']);
const MAX_COMMAND = 250;
const MAX_DETAIL = 5000;
const QUEUE_LIMIT = 24;
const TTL = 120_000;
const DELIVERY_TTL = 25_000; // sender waits about 30s for execution
const LEASE = 12_000;
const LIVE = 35_000;
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function validEnvelope(body) {
  return body && typeof body === 'object' && !Array.isArray(body) &&
    UUID.test(body.id || '') &&
    DEVICES.has(body.origin) && DEVICES.has(body.target) &&
    body.origin !== body.target &&
    typeof body.command === 'string' &&
    body.command.trim().length >= 1 && body.command.length <= MAX_COMMAND &&
    !/[\x00-\x1f]/.test(body.command) &&
    !/(?:pin|пароль|password|api key|ключ api|token|токен)/i.test(body.command);
}
export function validAck(body) {
  return body && typeof body==='object' && !Array.isArray(body) &&
    UUID.test(body.id || '') && DEVICES.has(body.device) &&
    typeof body.ok === 'boolean' &&
    typeof body.detail === 'string' && body.detail.length <= MAX_DETAIL &&
    !/[\x00-\x08\x0b\x0c\x0e-\x1f]/.test(body.detail);
}
export function validDevice(device) { return DEVICES.has(device); }
export function sendJson(data,status=200) {
  return new Response(JSON.stringify(data),{status,headers:{
    'content-type':'application/json; charset=utf-8',
    'cache-control':'no-store',
    'x-content-type-options':'nosniff'
  }});
}

export class DeviceRelay {
  constructor(state,env) {
    this.state=state;this.env=env;
    this.screen=new ScreenRelayState();
    this.fileInbox=new FileRelayState();
  }

  /** Durable Object storage expiry is a security property, not only a
   * read-time filter. An alarm erases old command text after inactivity. */
  async alarm() {
    const now=Date.now(),store=this.state.storage;
    const rows=await store.list({prefix:'m:'});
    for(const [key,value] of rows)
      if(now-value.created>TTL)await store.delete(key);
    const remaining=await store.list({prefix:'m:'});
    if(remaining.size && typeof store.setAlarm==='function') {
      const next=Math.min(...[...remaining.values()].map(x=>x.created+TTL+2000));
      await store.setAlarm(Math.max(now+1000,next));
    }
  }

  async fetch(request) {
    const url=new URL(request.url);
    const path=url.pathname;
    const now=Date.now();
    const store=this.state.storage;
    // Screenshots exist ONLY in short-lived object RAM. Do not touch durable
    // storage, history, logs or AI when serving screen viewer/frame calls.
    if(path.startsWith('/device/v1/screen/'))
      return this.screen.fetch(request);
    // File bytes live only in RAM and expire after 120 seconds.
    if(path.startsWith('/device/v1/files/'))
      return this.fileInbox.fetch(request);
    try {
      // A cleanup pass avoids unbounded retention without any background job.
      const rows=await store.list({prefix:'m:'});
      const stale=[];
      for(const [key,value] of rows)
        if(now-value.created>TTL)stale.push(key);
      if(stale.length)await Promise.all(stale.map(key=>store.delete(key)));

      if(path==='/device/v1/send' && request.method==='POST') {
        const body=await request.json();
        if(!validEnvelope(body))return sendJson({error:'Invalid device command'},400);
        const key='m:'+body.id;
        const existing=await store.get(key);
        if(existing) {
          if(existing.origin!==body.origin || existing.target!==body.target ||
             existing.command!==body.command)
            return sendJson({error:'Duplicate request ID with different payload'},409);
          return sendJson({id:body.id,state:existing.state});
        }
        const live=Array.from(rows.values()).filter(m=>now-m.created<=TTL &&
          m.target===body.target && m.state!=='done');
        if(live.length>=QUEUE_LIMIT)return sendJson({error:'Device queue full'},429);
        await store.put(key,{id:body.id,origin:body.origin,target:body.target,
          command:body.command,created:now,leaseUntil:0,state:'pending',
          ok:null,detail:''});
        // An alarm belongs to the EARLIEST expiry. New requests must not
        // push the alarm forward and retain old command/finance text longer
        // than its intended lifetime if every client goes offline.
        if(typeof store.setAlarm==='function') {
          const due=now+TTL+2000;
          const prior=typeof store.getAlarm==='function'
            ?await store.getAlarm():null;
          if(prior==null || prior>due)await store.setAlarm(due);
        }
        return sendJson({id:body.id,state:'pending'},202);
      }

      if(path==='/device/v1/poll' && request.method==='GET') {
        const device=url.searchParams.get('device');
        if(!validDevice(device))return sendJson({error:'Unknown device'},400);
        await store.put('live:'+device,now);
        const ready=[];
        for(const [key,item] of rows) {
          // Never deliver a stale queued financial command after its
          // sender has already timed out and perhaps checked/retried.
          // Keep result records up to 2 minutes for safe ACK reconciliation.
          if(item.target!==device || now-item.created>DELIVERY_TTL ||
             item.state==='done' || item.leaseUntil>now)continue;
          ready.push({key,item});
        }
        ready.sort((a,b)=>a.item.created-b.item.created);
        const items=[];
        for(const {key,item} of ready.slice(0,3)) {
          item.leaseUntil=now+LEASE;
          item.state='leased';
          await store.put(key,item);
          items.push({id:item.id,origin:item.origin,target:item.target,
            command:item.command});
        }
        return sendJson({items,serverTime:now});
      }

      if(path==='/device/v1/ack' && request.method==='POST') {
        const body=await request.json();
        if(!validAck(body))return sendJson({error:'Invalid acknowledgement'},400);
        const key='m:'+body.id;
        const item=await store.get(key);
        if(!item || now-item.created>TTL)return sendJson({error:'Command expired'},404);
        if(item.target!==body.device)return sendJson({error:'Wrong receiving device'},403);
        if(item.state!=='done') {
          item.state='done';item.ok=body.ok;item.detail=body.detail;
          item.leaseUntil=0;await store.put(key,item);
        }
        return sendJson({id:item.id,state:'done'});
      }

      if(path==='/device/v1/result' && request.method==='GET') {
        const id=url.searchParams.get('id'),origin=url.searchParams.get('origin');
        if(!UUID.test(id||'') || !validDevice(origin))
          return sendJson({error:'Invalid receipt query'},400);
        const item=await store.get('m:'+id);
        if(!item || item.origin!==origin || now-item.created>TTL)
          return sendJson({error:'Command unavailable'},404);
        return sendJson(item.state==='done'
          ?{id,done:true,ok:item.ok,detail:item.detail}
          :{id,done:false});
      }

      if(path==='/device/v1/online' && request.method==='GET') {
        const device=url.searchParams.get('device');
        if(!validDevice(device))return sendJson({error:'Unknown device'},400);
        const last=await store.get('live:'+device);
        return sendJson({device,online:Number.isFinite(last)&&now-last<LIVE});
      }
      return sendJson({error:'Not found'},404);
    }catch(e) {
      if(e instanceof SyntaxError)return sendJson({error:'Invalid JSON'},400);
      return sendJson({error:'Relay temporarily unavailable'},503);
    }
  }
}
