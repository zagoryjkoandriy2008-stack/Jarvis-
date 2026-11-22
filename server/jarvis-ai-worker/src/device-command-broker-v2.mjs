import {verifySignedRequest,DEFAULT_DEVICE_GRANTS} from './device-pairing-v2.mjs';
import {validateDeviceRequest,validateDeviceReceipt,classifyDeviceAction}
  from './device-protocol-v2.mjs';

/** Paired-only v2 typed command broker. Durable Object is the serialization
 * boundary. No arbitrary voice text, shell, clipboard, finance or screen data.
 * Phone is the authenticated transport delegate for a connected Watch7.
 */
const MAX_BODY=4096;
const MAX_ACTIVE=24;
const DELIVERY_TTL=25000;
const LEASE_MS=12000;
const RECEIPT_TTL=180000;
const HEARTBEAT_TTL=35000;
const TERMINAL=new Set(['completed','failed','unknown']);
function json(data,status=200){
  return new Response(JSON.stringify(data),{status,headers:{
    'content-type':'application/json; charset=utf-8',
    'cache-control':'no-store','x-content-type-options':'nosniff'}});
}
async function parsedBody(request) {
  if(request.method!=='POST')return null;
  const length=Number(request.headers.get('content-length')||0);
  if(!Number.isFinite(length)||length>MAX_BODY)return null;
  const raw=await request.text();
  if(raw.length>MAX_BODY)return null;
  try{
    const body=JSON.parse(raw);
    if(body===null||Array.isArray(body)||typeof body!=='object')return null;
    return {body,raw};
  }catch{return null;}
}
function stringId(x){return typeof x==='string' &&
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(x);}
function senderFor(origin,device){
  return origin===device || origin==='watch'&&device==='phone';
}
function allowRole(kind,origin,target) {
  if(!['computer','phone','watch'].includes(origin) ||
     !['computer','phone'].includes(target) || origin===target)return false;
  const scope=classifyDeviceAction(kind);
  if(scope==='forbiddenRemote')return false;
  if(origin==='watch'&&target!=='computer')return false;
  // Only Windows actions are wired in the first rollout.
  if(target!=='computer')return false;
  if(scope==='approvalRequired')
    return kind==='windows.lock'||kind==='monitor.off';
  if(kind==='macro.run')return false; // macro permissions require separate owner review
  return true;
}
export class DeviceCommandBrokerV2 {
  constructor(storage,clock=()=>Date.now()){this.store=storage;this.now=clock;}
  async _auth(request,raw,now){
    const id=request.headers.get('x-jarvis-device');
    if(id!=='computer'&&id!=='phone')return null;
    const entry=await this.store.get('v2:device:'+id);
    if(!entry?.enabled||!entry.publicKey)return null;
    if(!await verifySignedRequest(request,id,entry.publicKey,
      this.store,now,raw))return null;
    return id;
  }
  async fetch(request){
    const path=new URL(request.url).pathname;
    const parsed=await parsedBody(request);
    if(!parsed)return json({error:'Malformed body'},400);
    const {body,raw}=parsed,now=this.now(),store=this.store;
    const device=await this._auth(request,raw,now);
    if(!device)return json({error:'Paired device signature required'},403);

    if(path==='/device/v2/commands/send') {
      if(!validateDeviceRequest(body,now)||
         !senderFor(body.origin,device)||
         !allowRole(body.kind,body.origin,body.target))
        return json({error:'Command or permissions rejected'},403);
      const grants=await store.get('v2:grants:phone')||DEFAULT_DEVICE_GRANTS;
      if(device==='phone' && (
         (body.origin==='watch'&&!grants.watchDelegation) ||
         (classifyDeviceAction(body.kind)==='readOnly' && !grants.readOnly) ||
         ((body.kind.startsWith('media.')||body.kind==='volume.set') &&
           !grants.media) ||
         (body.kind==='application.open'&&!grants.apps) ||
         (['windows.lock','monitor.off'].includes(body.kind)&&!grants.locks)
      ))return json({error:'Device permission not granted'},403);
      const key='v2:command:'+body.requestId;
      const existing=await store.get(key);
      if(existing) {
        if(JSON.stringify(existing.command)!==JSON.stringify(body))
          return json({error:'Request ID collision'},409);
        return json({version:2,requestId:body.requestId,
          status:existing.status,expiresAt:existing.command.expiresAt},200);
      }
      const active=await store.list({prefix:'v2:command:'});
      if([...active.values()].filter(x=>x.command.expiresAt>now &&
         !TERMINAL.has(x.status)).length>=MAX_ACTIVE)
        return json({error:'Device queue full'},429);
      await store.put(key,{command:body,authenticatedBy:device,
        status:'accepted',errorCode:'',detail:'',updatedAt:now,
        leaseUntil:0,createdAt:now});
      await this._schedule(body.expiresAt+RECEIPT_TTL,now);
      return json({version:2,requestId:body.requestId,status:'accepted',
        expiresAt:body.expiresAt},202);
    }
    if(path==='/device/v2/commands/poll') {
      if(body.device!==device || device!=='computer')
        return json({error:'Target identity mismatch'},403);
      await store.put('v2:live:'+device,now);
      await this._schedule(now+HEARTBEAT_TTL,now);
      const queued=await store.list({prefix:'v2:command:'});
      // Revocation blocks even commands queued before the owner revoked a key.
      // Do not pass a stale sender's request onward to local execution.
      for(const item of queued.values()) {
        if(item.command.target!==device || TERMINAL.has(item.status))continue;
        const sender=await store.get('v2:device:'+item.authenticatedBy);
        if(!sender?.enabled) {
          item.status='failed';item.errorCode='DEVICE_REVOKED';
          item.detail='Доступ пристрою відкликано.';item.leaseUntil=0;
          item.updatedAt=now;
          await store.put('v2:command:'+item.command.requestId,item);
        }
      }
      const due=[...queued.values()]
        .filter(x=>x.command.target===device && x.command.expiresAt>now &&
          x.createdAt+DELIVERY_TTL>now &&
          !TERMINAL.has(x.status) && x.leaseUntil<=now)
        .sort((a,b)=>a.createdAt-b.createdAt).slice(0,3);
      const items=[];
      for(const item of due) {
        item.leaseUntil=now+LEASE_MS;item.status='running';
        item.updatedAt=now;
        await store.put('v2:command:'+item.command.requestId,item);
        items.push(item.command);
      }
      return json({version:2,status:'running',items,serverTime:now});
    }
    // The target MUST re-check owner permission before a side effect,
    // not only when a command was first enqueued or leased. This closes
    // the revocation/permissions-change gap for commands still in flight.
    if(path==='/device/v2/commands/authorize') {
      if(device!=='computer' || !stringId(body.requestId))
        return json({error:'Wrong target or request'},403);
      const item=await store.get('v2:command:'+body.requestId);
      if(!item || item.command.target!==device)
        return json({authorized:false,reason:'NOT_FOUND'});
      const eligible=item.status==='running' &&
        item.command.expiresAt>now &&
        item.createdAt+DELIVERY_TTL>now &&
        item.leaseUntil>now;
      if(!eligible)return json({authorized:false,reason:'NOT_ACTIVE'});
      const sender=await store.get('v2:device:'+item.authenticatedBy);
      if(!sender?.enabled)return json({authorized:false,reason:'DEVICE_REVOKED'});
      const grants=await store.get('v2:grants:phone')||DEFAULT_DEVICE_GRANTS;
      const kind=item.command.kind;
      const ownerPermit=item.authenticatedBy!=='phone' || (
        (item.command.origin!=='watch'||grants.watchDelegation) &&
        (classifyDeviceAction(kind)!=='readOnly'||grants.readOnly) &&
        (!(kind.startsWith('media.')||kind==='volume.set')||grants.media) &&
        (kind!=='application.open'||grants.apps) &&
        (!['windows.lock','monitor.off'].includes(kind)||grants.locks)
      );
      if(!ownerPermit)return json({authorized:false,reason:'PERMISSION_REVOKED'});
      return json({authorized:true,requestId:body.requestId,
        expiresAt:Math.min(item.leaseUntil,item.command.expiresAt)});
    }
    if(path==='/device/v2/commands/ack') {
      if(!validateDeviceReceipt(body) || device!=='computer' ||
         body.timestamp<now-30000 || body.timestamp>now+30000 ||
         !['running','completed','failed','unknown'].includes(body.status))
        return json({error:'Invalid receipt'},400);
      const key='v2:command:'+body.requestId;
      const item=await store.get(key);
      if(!item || item.command.target!==device)
        return json({error:'Receipt not found'},404);
      if(item.command.expiresAt<=now)return json({error:'Request expired'},410);
      if(!TERMINAL.has(item.status)) {
        item.status=body.status;item.updatedAt=now;
        if(body.status==='running')item.leaseUntil=now+LEASE_MS;
        else {
          item.errorCode=body.errorCode;
          item.detail=body.detail;
          item.leaseUntil=0;
        }
        await store.put(key,item);
      }
      return json({version:2,requestId:body.requestId,status:item.status});
    }
    if(path==='/device/v2/commands/result') {
      if(!stringId(body.requestId))return json({error:'Invalid request ID'},400);
      const item=await store.get('v2:command:'+body.requestId);
      if(!item || item.authenticatedBy!==device ||
         item.command.expiresAt+RECEIPT_TTL<=now)
        return json({error:'Receipt unavailable'},404);
      return json({version:2,requestId:body.requestId,
        status:item.command.expiresAt<=now && !TERMINAL.has(item.status)?
          'unknown':item.status,
        errorCode:item.errorCode,detail:TERMINAL.has(item.status)?item.detail:'',
        terminal:TERMINAL.has(item.status)});
    }
    if(path==='/device/v2/commands/heartbeat') {
      if(body.device!==device)return json({error:'Device mismatch'},403);
      await store.put('v2:live:'+device,now);
      await this._schedule(now+HEARTBEAT_TTL,now);
      return json({version:2,device,online:true,heartbeatAt:now});
    }
    if(path==='/device/v2/commands/status') {
      if(!['computer','phone','watch'].includes(body.device))
        return json({error:'Bad device'},400);
      // Never show a legacy v1 heartbeat as cryptographically verified v2.
      const last=await store.get('v2:live:'+body.device);
      const identity=await store.get('v2:device:'+body.device);
      const online=identity?.enabled===true &&
        Number.isFinite(last)&&now-last<HEARTBEAT_TTL;
      return json({version:2,device:body.device,online,
        state:online?'online':'offline',
        lastSeenMs:Number.isFinite(last)?last:null,ttlMs:HEARTBEAT_TTL});
    }
    return json({error:'Unknown v2 command route'},404);
  }
  async _schedule(expiry,now){
    if(typeof this.store.setAlarm!=='function')return;
    const current=typeof this.store.getAlarm==='function'?
      await this.store.getAlarm():null;
    const next=Math.max(expiry,now+1000);
    if(current==null||current>next)await this.store.setAlarm(next);
  }
}
