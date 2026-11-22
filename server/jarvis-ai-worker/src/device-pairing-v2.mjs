/** Device pairing v2: signed P-256 identity, short-lived one-time approval.
 * Worker still checks the user-owned bearer before reaching this module.
 * Pairing does NOT authorize existing v1 relay operations or remote AI.
 */
const encoder=new TextEncoder();
const ID=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const NONCE=/^[A-Za-z0-9_-]{16,64}$/;
const SPKI=/^[A-Za-z0-9+/]{120,220}={0,2}$/;
const CODE=/^[0-9]{7}$/;
const PAIR_TTL=300000;
const SIGN_TTL=30000;
const NONCE_TTL=65000;
export const DEFAULT_DEVICE_GRANTS=Object.freeze({
  readOnly:true,media:false,apps:false,locks:false,watchDelegation:false
});
function validGrants(grants) {
  return grants && typeof grants==='object' && !Array.isArray(grants) &&
    Object.keys(grants).sort().join(',')===
      Object.keys(DEFAULT_DEVICE_GRANTS).sort().join(',') &&
    Object.values(grants).every(v=>typeof v==='boolean');
}
function json(value,status=200) {
  return new Response(JSON.stringify(value),{status,headers:{
    'content-type':'application/json','cache-control':'no-store',
    'x-content-type-options':'nosniff'
  }});
}
function fromBase64(value) {
  if(typeof value!=='string'||!SPKI.test(value))return null;
  try {
    const str=atob(value);
    return Uint8Array.from(str,ch=>ch.charCodeAt(0));
  }catch{return null;}
}
function bytes64(value) {
  try{return Uint8Array.from(atob(value),ch=>ch.charCodeAt(0))}
  catch{return null}
}
async function importPublic(value) {
  const bytes=fromBase64(value);
  if(!bytes || bytes.length<80||bytes.length>160)return null;
  try{return await crypto.subtle.importKey('spki',bytes,
    {name:'ECDSA',namedCurve:'P-256'},false,['verify'])}
  catch{return null}
}
async function digest(text) {
  const buffer=await crypto.subtle.digest('SHA-256',encoder.encode(text));
  return Array.from(new Uint8Array(buffer),b=>b.toString(16).padStart(2,'0')).join('');
}
async function inputJson(request) {
  if(request.method!=='POST')return null;
  const length=Number(request.headers.get('content-length')||0);
  if(length>4096)return null;
  const raw=await request.text();
  if(raw.length>4096)return null;
  try {
    const body=JSON.parse(raw);
    return body && typeof body==='object' && !Array.isArray(body) ?
      {body,raw}:null;
  }catch{return null}
}
function uniqueId() {return crypto.randomUUID()}
function generateCode() {
  const bytes=new Uint32Array(1);
  crypto.getRandomValues(bytes);
  return (1000000+bytes[0]%9000000).toString();
}
export async function verifySignedRequest(request,device,spki,storage,now=Date.now(),rawBody=null) {
  if(!spki)return false;
  const stamped=request.headers.get('x-jarvis-timestamp');
  const nonce=request.headers.get('x-jarvis-nonce');
  const signature=request.headers.get('x-jarvis-signature');
  if(request.headers.get('x-jarvis-device')!==device ||
     !stamped || !/^\d{13}$/.test(stamped)||!Number.isSafeInteger(Number(stamped)) ||
     Math.abs(now-Number(stamped))>SIGN_TTL ||
     !nonce||!NONCE.test(nonce) ||
     !signature||signature.length>160)return false;
  const bytes=bytes64(signature);
  if(!bytes||bytes.length!==64)return false;
  const key=await importPublic(spki);
  if(!key)return false;
  const raw=typeof rawBody==='string'?rawBody:
    (request.method==='POST'?await request.clone().text():'');
  if(raw.length>4096)return false;
  const path=new URL(request.url).pathname;
  const canonical=[request.method,path,device,stamped,nonce,
    await digest(raw)].join('\n');
  let verified=false;
  try{verified=await crypto.subtle.verify({name:'ECDSA',hash:'SHA-256'},
     key,bytes,encoder.encode(canonical))}
  catch{return false}
  if(!verified)return false;
  const replayKey='v2:nonce:'+device+':'+nonce;
  const old=await storage.get(replayKey);
  if(old && old.expires>now)return false;
  await storage.put(replayKey,{expires:now+NONCE_TTL});
  // Durable Object serializes requests; schedule GC without moving earlier alarms.
  if(typeof storage.setAlarm==='function') {
    const current=typeof storage.getAlarm==='function'?
      await storage.getAlarm():null;
    const expiry=now+NONCE_TTL+1000;
    if(current==null||current>expiry)await storage.setAlarm(expiry);
  }
  return true;
}
export class DevicePairingV2 {
  constructor(storage,clock=()=>Date.now()){this.store=storage;this.clock=clock;}
  async fetch(request) {
    const path=new URL(request.url).pathname;
    const parsed=request.method==='POST'?await inputJson(request):null;
    const now=this.clock();
    const store=this.store;
    if(path==='/device/v2/pair/begin' && request.method==='POST') {
      if(!parsed)return json({error:'Invalid request'},400);
      const {body}=parsed;
      if(body.device!=='computer'||!await importPublic(body.publicKey))
        return json({error:'Invalid computer identity'},400);
      const existing=await store.get('v2:device:computer');
      if(existing && existing.enabled) {
        if(existing.publicKey!==body.publicKey ||
           !await verifySignedRequest(request,'computer',
             existing.publicKey,store,now,parsed.raw))
          return json({error:'Computer owner signature required'},403);
      }
      const active=await store.list({prefix:'v2:pending:'});
      if([...active.values()].filter(x=>x.expires>now).length>=3)
        return json({error:'Too many pending pairing attempts'},429);
      const id=uniqueId(),code=generateCode();
      const pending={id,created:now,expires:now+PAIR_TTL,attempts:0,
        computerPublicKey:body.publicKey,codeHash:await digest(id+':'+code),
        status:'waiting',phonePublicKey:null};
      await store.put('v2:pending:'+id,pending);
      if(typeof store.setAlarm==='function') {
        const previous=typeof store.getAlarm==='function'?
          await store.getAlarm():null;
        if(previous==null||previous>now+PAIR_TTL+1000)
          await store.setAlarm(now+PAIR_TTL+1000);
      }
      return json({pairingId:id,code,expiresInMs:PAIR_TTL},201);
    }
    if(path==='/device/v2/pair/request' && request.method==='POST') {
      if(!parsed)return json({error:'Invalid request'},400);
      const {body}=parsed;
      if(!ID.test(body.pairingId||'') || !CODE.test(body.code||'') ||
         body.device!=='phone'||!await importPublic(body.publicKey))
        return json({error:'Invalid pairing request'},400);
      const pending=await store.get('v2:pending:'+body.pairingId);
      if(!pending||pending.expires<=now||pending.attempts>=5||
         pending.status!=='waiting')
        return json({error:'Pairing unavailable'},404);
      if(pending.codeHash!==await digest(body.pairingId+':'+body.code)) {
        pending.attempts++;
        await store.put('v2:pending:'+body.pairingId,pending);
        return json({error:'Invalid pairing code'},403);
      }
      pending.phonePublicKey=body.publicKey;
      pending.status='requested';
      await store.put('v2:pending:'+body.pairingId,pending);
      return json({pairingId:body.pairingId,state:'waiting-for-windows-approval'},202);
    }
    if(path==='/device/v2/pair/pending' && request.method==='POST') {
      if(!parsed || !ID.test(parsed.body.pairingId||''))
        return json({error:'Invalid pairing query'},400);
      const pending=await store.get('v2:pending:'+parsed.body.pairingId);
      if(!pending || pending.expires<=now)return json({error:'Pairing unavailable'},404);
      const computer=await store.get('v2:device:computer');
      const ownerKey=computer?.enabled?computer.publicKey:pending.computerPublicKey;
      if(!await verifySignedRequest(request,'computer',ownerKey,store,now,parsed.raw))
        return json({error:'Windows signature required'},403);
      return json({pairingId:pending.id,state:pending.status,
        phoneFingerprint:pending.phonePublicKey?
          (await digest(pending.phonePublicKey)).slice(0,16).toUpperCase():null,
        expiresInMs:Math.max(0,pending.expires-now)});
    }
    if(path==='/device/v2/pair/approve' && request.method==='POST') {
      if(!parsed)return json({error:'Invalid approval'},400);
      const {body}=parsed;
      if(!ID.test(body.pairingId||'') || typeof body.approve!=='boolean')
        return json({error:'Invalid approval'},400);
      const pending=await store.get('v2:pending:'+body.pairingId);
      if(!pending || pending.expires<=now || pending.status!=='requested')
        return json({error:'Pairing unavailable'},404);
      const computer=await store.get('v2:device:computer');
      const ownerKey=computer?.enabled?computer.publicKey:pending.computerPublicKey;
      if(!await verifySignedRequest(request,'computer',ownerKey,store,now,parsed.raw))
        return json({error:'Invalid Windows approval signature'},403);
      if(!body.approve) {
        await store.delete('v2:pending:'+body.pairingId);
        return json({approved:false});
      }
      // A previously authorized phone cannot be silently replaced.
      const previousPhone=await store.get('v2:device:phone');
      if(previousPhone?.enabled)return json({error:'Existing phone must be revoked'},409);
      await store.put('v2:device:computer',{publicKey:ownerKey,
        enabled:true,pairedAt:now});
      await store.put('v2:device:phone',{publicKey:pending.phonePublicKey,
        enabled:true,pairedAt:now});
      await store.put('v2:grants:phone',{...DEFAULT_DEVICE_GRANTS});
      await store.delete('v2:pending:'+body.pairingId);
      return json({approved:true,devices:['computer','phone']},200);
    }
    if((path==='/device/v2/pair/grants/get' ||
       path==='/device/v2/pair/grants/set') && request.method==='POST') {
      if(!parsed || parsed.body.device!=='phone')
        return json({error:'Invalid grant target'},400);
      const computer=await store.get('v2:device:computer');
      if(!computer?.enabled ||
         !await verifySignedRequest(request,'computer',
           computer.publicKey,store,now,parsed.raw))
        return json({error:'Owner signature required'},403);
      const phone=await store.get('v2:device:phone');
      if(!phone?.enabled)return json({error:'Pixel not paired'},409);
      if(path.endsWith('/set')){
        if(!validGrants(parsed.body.permissions))
          return json({error:'Invalid grant schema'},400);
        await store.put('v2:grants:phone',{...parsed.body.permissions});
      }
      return json({device:'phone',permissions:
        await store.get('v2:grants:phone')||{...DEFAULT_DEVICE_GRANTS}});
    }
    if(path==='/device/v2/pair/list' && request.method==='POST') {
      if(!parsed)return json({error:'Invalid request'},400);
      const computer=await store.get('v2:device:computer');
      if(!computer?.enabled ||
         !await verifySignedRequest(request,'computer',computer.publicKey,store,now,parsed.raw))
        return json({error:'Unauthorized device'},403);
      const phone=await store.get('v2:device:phone');
      return json({devices:[
        {name:'computer',enabled:true,pairedAt:computer.pairedAt},
        {name:'phone',enabled:phone?.enabled===true,pairedAt:phone?.pairedAt||null}
      ]});
    }
    if(path==='/device/v2/pair/revoke' && request.method==='POST') {
      if(!parsed||parsed.body.device!=='phone')
        return json({error:'Invalid revocation'},400);
      const computer=await store.get('v2:device:computer');
      if(!computer?.enabled ||
         !await verifySignedRequest(request,'computer',computer.publicKey,store,now,parsed.raw))
        return json({error:'Unauthorized revocation'},403);
      const phone=await store.get('v2:device:phone');
      if(phone)await store.put('v2:device:phone',{...phone,enabled:false,
        revokedAt:now});
      await store.delete('v2:grants:phone');
      await store.delete('v2:live:phone');
      return json({revoked:true,device:'phone'});
    }
    if(path==='/device/v2/pair/verify' && request.method==='POST') {
      if(!parsed)return json({error:'Invalid request'},400);
      const device=request.headers.get('x-jarvis-device');
      if(device!=='computer'&&device!=='phone')
        return json({error:'Unknown device'},403);
      const identity=await store.get('v2:device:'+device);
      if(!identity?.enabled ||
         !await verifySignedRequest(request,device,identity.publicKey,store,now,parsed.raw))
        return json({error:'Not paired or signature invalid'},403);
      return json({verified:true,device});
    }
    return json({error:'Not found'},404);
  }
}
