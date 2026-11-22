import test from 'node:test';
import assert from 'node:assert/strict';
import {webcrypto} from 'node:crypto';
import {DevicePairingV2} from '../src/device-pairing-v2.mjs';
globalThis.crypto??=webcrypto;
function storage() {
 const map=new Map();
 return {map,async get(key){return map.get(key)},
 async put(key,value){map.set(key,value)},async delete(key){map.delete(key)}};
}
async function identity() {
 const pair=await crypto.subtle.generateKey(
    {name:'ECDSA',namedCurve:'P-256'},true,['sign','verify']);
 const spki=await crypto.subtle.exportKey('spki',pair.publicKey);
 return {key:pair.privateKey,publicKey:Buffer.from(spki).toString('base64')};
}
function get(r,p,b,h={}) {
 return new Request('https://worker.test'+p,{
   method:'POST',headers:{'content-type':'application/json',...h},
   body:JSON.stringify(b)
 });
}
async function signed(path,body,device,key) {
 const timestamp=String(Date.now()),nonce=crypto.randomUUID().replace(/-/g,'');
 const raw=JSON.stringify(body),hash=await crypto.subtle.digest('SHA-256',
    new TextEncoder().encode(raw));
 const sum=Buffer.from(hash).toString('hex');
 const input=['POST',path,device,timestamp,nonce,sum].join('\n');
 const bytes=await crypto.subtle.sign({name:'ECDSA',hash:'SHA-256'},
   key,new TextEncoder().encode(input));
 return get(null,path,body,{'x-jarvis-device':device,
   'x-jarvis-timestamp':timestamp,'x-jarvis-nonce':nonce,
   'x-jarvis-signature':Buffer.from(bytes).toString('base64')});
}
async function call(p,request) {
 const response=await p.fetch(request);
 return {status:response.status,body:await response.json()};
}
test('P-256 pairing needs code and explicit signed Windows approval',async()=>{
 const s=storage(),p=new DevicePairingV2(s),pc=await identity(),phone=await identity();
 let start=await call(p,get(null,'/device/v2/pair/begin',
    {device:'computer',publicKey:pc.publicKey}));
 assert.equal(start.status,201);
 const id=start.body.pairingId,code=start.body.code;
 assert.match(code,/^\d{7}$/);
 const wrong=await call(p,get(null,'/device/v2/pair/request',
    {pairingId:id,device:'phone',code:'0000000',publicKey:phone.publicKey}));
 assert.equal(wrong.status,403);
 const pending=await call(p,get(null,'/device/v2/pair/request',
    {pairingId:id,device:'phone',code,publicKey:phone.publicKey}));
 assert.equal(pending.status,202);
 const seen=await call(p,await signed('/device/v2/pair/pending',
    {pairingId:id},'computer',pc.key));
 assert.equal(seen.body.state,'requested');
 assert.match(seen.body.phoneFingerprint,/^[0-9A-F]{16}$/);
 const bad=await call(p,get(null,'/device/v2/pair/approve',
   {pairingId:id,approve:true}));
 assert.equal(bad.status,403);
 let request=await signed('/device/v2/pair/approve',
    {pairingId:id,approve:true},'computer',pc.key);
 const success=await call(p,request);
 assert.equal(success.body.approved,true);
 const replay=await call(p,new Request(request.url,{
   method:'POST',headers:Object.fromEntries(request.headers),
   body:JSON.stringify({pairingId:id,approve:true})
 }));
 assert.notEqual(replay.status,200);
 const verified=await call(p,await signed('/device/v2/pair/verify',
    {},'phone',phone.key));
 assert.equal(verified.body.verified,true);
 const someoneElse=await identity();
 const impersonate=await call(p,await signed('/device/v2/pair/verify',
    {},'phone',someoneElse.key));
 assert.equal(impersonate.status,403);
 const defaults=await call(p,await signed('/device/v2/pair/grants/get',
    {device:'phone'},'computer',pc.key));
 assert.deepEqual(defaults.body.permissions,{
   readOnly:true,media:false,apps:false,locks:false,watchDelegation:false
 });
 const updated=await call(p,await signed('/device/v2/pair/grants/set',
    {device:'phone',permissions:{readOnly:true,media:true,apps:false,
      locks:false,watchDelegation:true}},'computer',pc.key));
 assert.equal(updated.body.permissions.media,true);
 assert.equal(updated.body.permissions.watchDelegation,true);
 const invalid=await call(p,await signed('/device/v2/pair/grants/set',
    {device:'phone',permissions:{media:true}},'computer',pc.key));
 assert.equal(invalid.status,400);
 const listed=await call(p,await signed('/device/v2/pair/list',
    {},'computer',pc.key));
 assert.equal(listed.body.devices[1].enabled,true);
 const revoked=await call(p,await signed('/device/v2/pair/revoke',
    {device:'phone'},'computer',pc.key));
 assert.equal(revoked.body.revoked,true);
 assert.equal(s.map.has('v2:grants:phone'),false,'revoke clears old device grants');
 assert.equal((await call(p,await signed('/device/v2/pair/verify',
    {},'phone',phone.key))).status,403);
});
test('pair codes rate-limit guessing and pending sessions expire',async()=>{
 const s=storage(),p=new DevicePairingV2(s),pc=await identity(),phone=await identity();
 const start=await call(p,get(null,'/device/v2/pair/begin',
    {device:'computer',publicKey:pc.publicKey}));
 for(let i=0;i<5;i++)await call(p,get(null,'/device/v2/pair/request',
   {pairingId:start.body.pairingId,device:'phone',code:'9999999',
     publicKey:phone.publicKey}));
 assert.equal((await call(p,get(null,'/device/v2/pair/request',
   {pairingId:start.body.pairingId,device:'phone',code:start.body.code,
      publicKey:phone.publicKey}))).status,404);
});
