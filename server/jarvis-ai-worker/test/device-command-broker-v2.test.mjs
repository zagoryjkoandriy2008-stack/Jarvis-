import test from 'node:test';
import assert from 'node:assert/strict';
import {webcrypto} from 'node:crypto';
import {DeviceCommandBrokerV2} from '../src/device-command-broker-v2.mjs';
globalThis.crypto??=webcrypto;
const id='fd72d926-1184-4702-8ee4-d53c8f30cbbb';
const now=1730000000000;
function fakeStorage(){
 const map=new Map();return {
   async get(k){return map.get(k)},async put(k,v){map.set(k,v)},
   async delete(k){map.delete(k)},async list({prefix}){return new Map(
    [...map].filter(([key])=>key.startsWith(prefix)))}
 };
}
async function keypair(){
 const pair=await crypto.subtle.generateKey({name:'ECDSA',namedCurve:'P-256'},
   true,['sign','verify']);
 const spki=await crypto.subtle.exportKey('spki',pair.publicKey);
 return {privateKey:pair.privateKey,spki:Buffer.from(spki).toString('base64')};
}
async function signed(path,body,device,key,nowMs=now,nonce=''){
 const raw=JSON.stringify(body);
 const n=nonce||Buffer.from(crypto.getRandomValues(new Uint8Array(16))).toString('hex');
 const hash=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(raw));
 const canonical=['POST',path,device,String(nowMs),n,Buffer.from(hash).toString('hex')].join('\n');
 const sig=await crypto.subtle.sign({name:'ECDSA',hash:'SHA-256'},
  key,new TextEncoder().encode(canonical));
 return new Request('https://example.test'+path,{method:'POST',
  headers:{'content-type':'application/json','x-jarvis-device':device,
   'x-jarvis-timestamp':String(nowMs),'x-jarvis-nonce':n,
   'x-jarvis-signature':Buffer.from(sig).toString('base64')},body:raw});
}
function command(kind='system.status',origin='phone'){
 return {version:2,requestId:id,origin,target:'computer',kind,payload:{},
  createdAt:now-1000,expiresAt:now+60000,requiresApproval:false,replyTo:origin};
}
async function post(service,path,body,device,key,stamp=now){
 const response=await service.fetch(await signed(path,body,device,key,stamp));
 return {status:response.status,result:await response.json()};
}
test('paired-only v2 delivery, dedup, running/completed receipt and status',async()=>{
 const storage=fakeStorage(),phone=await keypair(),pc=await keypair();
 await storage.put('v2:device:phone',{enabled:true,publicKey:phone.spki});
 await storage.put('v2:device:computer',{enabled:true,publicKey:pc.spki});
 const broker=new DeviceCommandBrokerV2(storage,()=>now);
 const sent=await post(broker,'/device/v2/commands/send',command(),'phone',phone.privateKey);
 assert.equal(sent.status,202);
 assert.equal(sent.result.status,'accepted');
 const again=await post(broker,'/device/v2/commands/send',command(),'phone',phone.privateKey);
 assert.equal(again.status,200,'dedup does not execute twice');
 const poll=await post(broker,'/device/v2/commands/poll',{device:'computer'},'computer',pc.privateKey);
 assert.equal(poll.result.items.length,1);assert.equal(poll.result.items[0].requestId,id);
 const running=await post(broker,'/device/v2/commands/result',{requestId:id},'phone',phone.privateKey);
 assert.equal(running.result.status,'running');assert.equal(running.result.terminal,false);
 const reply={version:2,requestId:id,status:'completed',detail:'Готово',
   errorCode:'',timestamp:now};
 const ack=await post(broker,'/device/v2/commands/ack',reply,'computer',pc.privateKey);
 assert.equal(ack.result.status,'completed');
 const receipt=await post(broker,'/device/v2/commands/result',{requestId:id},'phone',phone.privateKey);
 assert.equal(receipt.result.status,'completed');assert.equal(receipt.result.detail,'Готово');
 const second=await post(broker,'/device/v2/commands/poll',{device:'computer'},'computer',pc.privateKey);
 assert.equal(second.result.items.length,0,'completed never redelivered');
});
test('v2 does not accept v1 bearer, wrong signatures, role spoofing, risk escalation or expired commands',async()=>{
 const storage=fakeStorage(),phone=await keypair(),pc=await keypair();
 await storage.put('v2:device:phone',{enabled:true,publicKey:phone.spki});
 await storage.put('v2:device:computer',{enabled:true,publicKey:pc.spki});
 const broker=new DeviceCommandBrokerV2(storage,()=>now);
 assert.equal((await broker.fetch(new Request('https://example.test/device/v2/commands/send',{
  method:'POST',headers:{authorization:'Bearer old-token'},
  body:JSON.stringify(command())}))).status,403);
 assert.equal((await post(broker,'/device/v2/commands/send',command(),'phone',pc.privateKey)).status,403);
 assert.equal((await post(broker,'/device/v2/commands/send',command(),'computer',pc.privateKey)).status,403);
 assert.equal((await post(broker,'/device/v2/commands/send',{...command(),kind:'shell.run'},'phone',phone.privateKey)).status,403);
 assert.equal((await post(broker,'/device/v2/commands/send',{...command('windows.lock'),requiresApproval:false},'phone',phone.privateKey)).status,403);
 assert.equal((await post(broker,'/device/v2/commands/send',{...command('macro.run'),payload:{name:'hello'}},'phone',phone.privateKey)).status,403);
 assert.equal((await post(broker,'/device/v2/commands/send',{...command(),expiresAt:now-10},'phone',phone.privateKey)).status,403);
 assert.equal((await post(broker,'/device/v2/commands/send',{...command(),origin:'watch',replyTo:'watch'},'computer',pc.privateKey)).status,403);
 const spoofedAck=await post(broker,'/device/v2/commands/ack',{version:2,requestId:id,
  status:'completed',errorCode:'',detail:'',timestamp:now},'phone',phone.privateKey);
 assert.notEqual(spoofedAck.status,200);
});
test('revoked phone cannot send, poll or read receipts; Watch is delegated only by paired Pixel',async()=>{
 const storage=fakeStorage(),phone=await keypair(),pc=await keypair();
 await storage.put('v2:device:phone',{enabled:true,publicKey:phone.spki});
 await storage.put('v2:device:computer',{enabled:true,publicKey:pc.spki});
 const broker=new DeviceCommandBrokerV2(storage,()=>now);
 const watch={...command(),origin:'watch',replyTo:'watch'};
 assert.equal((await post(broker,'/device/v2/commands/send',watch,'phone',phone.privateKey)).status,403);
 await storage.put('v2:grants:phone',{readOnly:true,media:false,apps:false,
   locks:false,watchDelegation:true});
 assert.equal((await post(broker,'/device/v2/commands/send',watch,'phone',phone.privateKey)).status,202);
 await storage.put('v2:device:phone',{enabled:false,publicKey:phone.spki});
 assert.equal((await post(broker,'/device/v2/commands/send',command(),'phone',phone.privateKey)).status,403);
 assert.equal((await post(broker,'/device/v2/commands/result',{requestId:id},'phone',phone.privateKey)).status,403);
 const poll=await post(broker,'/device/v2/commands/poll',{device:'computer'},'computer',pc.privateKey);
 assert.equal(poll.result.items.length,0,'revoked sender must not deliver queued commands');
 assert.equal((await storage.get('v2:command:'+id)).status,'failed');
 assert.equal((await storage.get('v2:command:'+id)).errorCode,'DEVICE_REVOKED');
});
test('stale v2 requests are unknown, never newly delivered',async()=>{
 let tick=now;const storage=fakeStorage(),phone=await keypair(),pc=await keypair();
 await storage.put('v2:device:phone',{enabled:true,publicKey:phone.spki});
 await storage.put('v2:device:computer',{enabled:true,publicKey:pc.spki});
 const broker=new DeviceCommandBrokerV2(storage,()=>tick);
 await post(broker,'/device/v2/commands/send',command(),'phone',phone.privateKey);
 tick+=70000;
 const poll=await post(broker,'/device/v2/commands/poll',{device:'computer'},'computer',pc.privateKey,tick);
 assert.equal(poll.result.items.length,0);
 const result=await post(broker,'/device/v2/commands/result',{requestId:id},'phone',phone.privateKey,tick);
 assert.equal(result.result.status,'unknown');
});

test('least privilege grants are fail-closed and cannot be inherited after revocation',async()=>{
 const storage=fakeStorage(),phone=await keypair(),pc=await keypair();
 await storage.put('v2:device:phone',{enabled:true,publicKey:phone.spki});
 await storage.put('v2:device:computer',{enabled:true,publicKey:pc.spki});
 const broker=new DeviceCommandBrokerV2(storage,()=>now);
 const media={...command('media.next'),requestId:crypto.randomUUID()};
 const openApp={...command('application.open'),requestId:crypto.randomUUID(),
   payload:{name:'notepad'}};
 const lock={...command('windows.lock'),requestId:crypto.randomUUID(),
   requiresApproval:true};
 for(const c of [media,openApp,lock])
   assert.equal((await post(broker,'/device/v2/commands/send',
     c,'phone',phone.privateKey)).status,403,'grants default OFF');
 await storage.put('v2:grants:phone',{readOnly:true,media:true,apps:true,
   locks:true,watchDelegation:false});
 for(const c of [media,openApp,lock])
   assert.equal((await post(broker,'/device/v2/commands/send',
     c,'phone',phone.privateKey)).status,202,'explicit grant permits');
 await storage.delete('v2:grants:phone');
 for(const c of [media,openApp,lock])
   assert.equal((await post(broker,'/device/v2/commands/send',
     {...c,requestId:crypto.randomUUID()},'phone',phone.privateKey)).status,403);
});

test('legacy v1 routes are denied after explicit owner revocation, not only typed v2',async()=>{
 const {DeviceRelay}=await import('../src/device-relay.mjs');
 const storage=fakeStorage();
 storage.put('v2:legacy-disabled',true);
 const relay=new DeviceRelay({storage},{});
 for(const path of ['/device/v1/poll?device=computer','/device/v1/online?device=phone',
    '/device/v1/screen/state','/device/v1/files/latest?target=phone']){
   const response=await relay.fetch(new Request('https://worker.test'+path));
   assert.equal(response.status,403,path);
 }
});

test('post-lease authorization blocks revoked phones and changed capabilities',async()=>{
 const storage=fakeStorage(),phone=await keypair(),pc=await keypair();
 await storage.put('v2:device:phone',{enabled:true,publicKey:phone.spki});
 await storage.put('v2:device:computer',{enabled:true,publicKey:pc.spki});
 const broker=new DeviceCommandBrokerV2(storage,()=>now);
 const request=command();
 assert.equal((await post(broker,'/device/v2/commands/send',
   request,'phone',phone.privateKey)).status,202);
 const first=await post(broker,'/device/v2/commands/poll',
   {device:'computer'},'computer',pc.privateKey);
 assert.equal(first.result.items.length,1);
 let checked=await post(broker,'/device/v2/commands/authorize',
   {requestId:request.requestId},'computer',pc.privateKey);
 assert.equal(checked.result.authorized,true);
 await storage.put('v2:device:phone',{enabled:false,publicKey:phone.spki});
 checked=await post(broker,'/device/v2/commands/authorize',
   {requestId:request.requestId},'computer',pc.privateKey);
 assert.equal(checked.result.authorized,false);
 assert.equal(checked.result.reason,'DEVICE_REVOKED');

 // A permissions change made after queuing must also block execution.
 await storage.put('v2:device:phone',{enabled:true,publicKey:phone.spki});
 await storage.put('v2:grants:phone',{
   readOnly:true,media:true,apps:false,locks:false,watchDelegation:false});
 const media={...command('media.next'),requestId:crypto.randomUUID()};
 assert.equal((await post(broker,'/device/v2/commands/send',
   media,'phone',phone.privateKey)).status,202);
 const leased=await post(broker,'/device/v2/commands/poll',
   {device:'computer'},'computer',pc.privateKey);
 assert.ok(leased.result.items.some(x=>x.requestId===media.requestId));
 await storage.put('v2:grants:phone',{
   readOnly:true,media:false,apps:false,locks:false,watchDelegation:false});
 checked=await post(broker,'/device/v2/commands/authorize',
   {requestId:media.requestId},'computer',pc.privateKey);
 assert.equal(checked.result.authorized,false);
 assert.equal(checked.result.reason,'PERMISSION_REVOKED');

 const forged=await post(broker,'/device/v2/commands/authorize',
   {requestId:media.requestId},'phone',phone.privateKey);
 assert.equal(forged.status,403,'only target Windows can authorize execution');
 const unknown=await post(broker,'/device/v2/commands/authorize',
   {requestId:crypto.randomUUID()},'computer',pc.privateKey);
 assert.equal(unknown.result.authorized,false);
 assert.equal(unknown.result.reason,'NOT_FOUND');
});
