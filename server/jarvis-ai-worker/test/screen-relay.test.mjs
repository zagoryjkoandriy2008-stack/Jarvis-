import test from 'node:test';
import assert from 'node:assert/strict';
import {ScreenRelayState} from '../src/screen-relay.mjs';

const id='35c8d264-bca6-4a33-9155-84224d2fef09';
const alt='35c8d264-bca6-4a33-9155-84224d2fef0a';
const jpeg=new Uint8Array([0xff,0xd8,...Array(25).fill(43),0xff,0xd9]);

async function jsonCall(screen,method,path,body) {
  const request=new Request('https://worker.test'+path,{
    method,
    headers:body?{'content-type':'application/json'}:{},
    body:body?JSON.stringify(body):undefined
  });
  const response=await screen.fetch(request);
  return {status:response.status,body:await response.json()};
}
async function upload(screen,session,bytes=jpeg,contentType='image/jpeg') {
  return screen.fetch(new Request(
    'https://worker.test/device/v1/screen/frame?session='+session,{
      method:'POST',headers:{'content-type':contentType},
      body:bytes
    }));
}

test('screen is OFF without explicit phone viewer; only phone may request',async()=>{
  let now=100_000;
  const screen=new ScreenRelayState(()=>now);
  const initial=await jsonCall(screen,'GET','/device/v1/screen/state');
  assert.equal(initial.body.active,false);
  assert.equal((await jsonCall(screen,'POST','/device/v1/screen/view',
    {device:'watch',session:id})).status,400);
  assert.equal((await jsonCall(screen,'POST','/device/v1/screen/view',
    {device:'phone',session:'bad'})).status,400);
  assert.equal((await upload(screen,id)).status,403);
  assert.equal(screen.frame,null);
  assert.equal(screen.session,null);
});

test('view only + short-lived JPEG frames, without storage',async()=>{
  let now=100_000;
  const screen=new ScreenRelayState(()=>now);
  let r=await jsonCall(screen,'POST','/device/v1/screen/view',
    {device:'phone',session:id});
  assert.equal(r.status,200);
  const state=await jsonCall(screen,'GET','/device/v1/screen/state');
  assert.equal(state.body.session,id);
  assert.equal(state.body.active,true);
  assert.equal((await upload(screen,alt)).status,403);
  assert.equal((await upload(screen,id,jpeg,'image/png')).status,415);
  assert.equal((await upload(screen,id,new Uint8Array(30))).status,413);
  r=await upload(screen,id);
  assert.equal(r.status,202);
  assert.equal(screen.frame.byteLength,jpeg.length);
  const served=await screen.fetch(new Request(
    'https://worker.test/device/v1/screen/frame?session='+id));
  assert.equal(served.status,200);
  assert.equal(served.headers.get('content-type'),'image/jpeg');
  assert.match(served.headers.get('cache-control'),/no-store/);
  assert.deepEqual(new Uint8Array(await served.arrayBuffer()),jpeg);
  assert.equal((await upload(screen,id)).status,429,'throttled uploads');
  now+=600;
  assert.equal((await upload(screen,id)).status,202,'next frame after interval');
  // The screen relay owns no persistent storage object or put/get calls.
  assert.equal('state' in screen,false);
  assert.equal('storage' in screen,false);
});

test('stop revokes frame, second viewer rejected until stopped',async()=>{
  let now=100_000;
  const screen=new ScreenRelayState(()=>now);
  await jsonCall(screen,'POST','/device/v1/screen/view',
    {device:'phone',session:id});
  assert.equal((await jsonCall(screen,'POST','/device/v1/screen/view',
    {device:'phone',session:alt})).status,409);
  await upload(screen,id);
  const wrongStop=await jsonCall(screen,'POST','/device/v1/screen/stop',
    {session:alt});
  assert.equal(wrongStop.body.active,false);
  assert.equal(screen.active(),true,'other viewer cannot stop this session');
  const stop=await jsonCall(screen,'POST','/device/v1/screen/stop',
    {session:id});
  assert.equal(stop.status,200);
  assert.equal(screen.session,null);
  assert.equal(screen.frame,null);
  assert.equal((await upload(screen,id)).status,403);
  assert.equal((await jsonCall(screen,'GET',
    '/device/v1/screen/state')).body.active,false);
});

test('viewer heartbeat expiry deletes frame in memory automatically',async()=>{
  let now=100_000;
  const screen=new ScreenRelayState(()=>now);
  await jsonCall(screen,'POST','/device/v1/screen/view',
    {device:'phone',session:id});
  await upload(screen,id);
  now+=8001;
  assert.equal((await jsonCall(screen,'GET',
    '/device/v1/screen/state')).body.active,false);
  assert.equal(screen.frame,null);
  assert.equal(screen.session,null);
  assert.equal((await upload(screen,id)).status,403);
});

test('oversized JPEG rejected before memory retention',async()=>{
  let now=100_000;
  const screen=new ScreenRelayState(()=>now);
  await jsonCall(screen,'POST','/device/v1/screen/view',
    {device:'phone',session:id});
  const massive=new Uint8Array(240_100);
  massive[0]=0xff;massive[1]=0xd8;
  massive[massive.length-2]=0xff;massive[massive.length-1]=0xd9;
  const r=await upload(screen,id,massive);
  assert.equal(r.status,413);
  assert.equal(screen.frame,null);
});

test('stale images are never returned as live',async()=>{
  let now=100_000;
  const screen=new ScreenRelayState(()=>now);
  await jsonCall(screen,'POST','/device/v1/screen/view',
    {device:'phone',session:id});
  await upload(screen,id);
  now+=5100;
  // viewer still active, but latest frame has aged out
  const r=await screen.fetch(new Request(
    'https://worker.test/device/v1/screen/frame?session='+id));
  assert.equal(r.status,404);
});

test('Windows owner revocation drops the active viewer and frame instantly',async()=>{
  let now=100_000;
  const screen=new ScreenRelayState(()=>now);
  await jsonCall(screen,'POST','/device/v1/screen/view',
    {device:'phone',session:id});
  await upload(screen,id);
  assert.ok(screen.frame);
  const revoked=await jsonCall(screen,'POST','/device/v1/screen/revoke',
    {session:id});
  assert.equal(revoked.status,200);
  assert.equal(screen.frame,null);
  assert.equal(screen.session,null);
  assert.equal((await screen.fetch(new Request(
    'https://worker.test/device/v1/screen/frame?session='+id))).status,403);
  assert.equal((await jsonCall(screen,'GET',
    '/device/v1/screen/state')).body.active,false);
});
