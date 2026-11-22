import test from 'node:test';
import assert from 'node:assert/strict';
import {ScreenControlState,validScreenInput} from '../src/screen-control.mjs';
import {ScreenRelayState} from '../src/screen-relay.mjs';
const a='35c8d264-bca6-4a33-9155-84224d2fef11',b='35c8d264-bca6-4a33-9155-84224d2fef12';
const c='35c8d264-bca6-4a33-9155-84224d2fef13';
const event=(kind,rest={})=>({id:a,kind,...rest});
async function call(relay,method,path,body){
  const res=await relay.fetch(new Request('https://example.test'+path,{
    method,headers:body?{'content-type':'application/json'}:{},
    body:body?JSON.stringify(body):undefined
  }));
  return {code:res.status,json:await res.json()};
}
function route(path,session=a){return '/device/v1/screen/'+path+'?session='+session}
test('normalized clicks, drags, shortcuts and bounded text only',()=>{
  assert.ok(validScreenInput(event('tap',{x:0.5,y:0})));
  assert.ok(validScreenInput(event('double',{x:1,y:1})));
  assert.ok(validScreenInput(event('right',{x:0.5,y:0.5})));
  assert.ok(validScreenInput(event('drag',{x:0,y:0,x2:1,y2:1})));
  assert.ok(validScreenInput(event('scroll',{delta:-8})));
  assert.ok(validScreenInput(event('text',{text:'Привіт ❤️'})));
  assert.ok(validScreenInput(event('key',{key:'CTRL_A'})));
  for(const item of [
    event('tap',{x:2,y:0}),event('drag',{x:0,y:0,x2:NaN,y2:1}),
    event('scroll',{delta:100}),event('text',{text:'x'.repeat(161)}),
    event('text',{text:'password\n next command'}),
    event('key',{key:'CTRL_ALT_DEL'}),event('key',{key:'POWERSHELL'}),
    event('shell',{text:'shutdown'}),{...event('tap',{x:0.5,y:0.5}),id:'bad'}
  ]) assert.equal(validScreenInput(item),false,JSON.stringify(item).slice(0,80));
});
test('all remote input requires explicit Windows approval after active Pixel viewer',async()=>{
  let now=100000;
  const relay=new ScreenRelayState(()=>now);
  const state=route('control/state');
  assert.equal((await call(relay,'GET',state)).code,403);
  assert.equal((await call(relay,'POST',route('control/input'),event('tap',{x:0.3,y:0.4}))).code,403);
  await call(relay,'POST','/device/v1/screen/view',{device:'phone',session:a});
  assert.equal((await call(relay,'POST',route('control/input'),event('tap',{x:0.3,y:0.4}))).code,403);
  const request=await call(relay,'POST',route('control/request'));
  assert.equal(request.json.controlRequested,true);
  assert.equal((await call(relay,'POST',route('control/input'),event('tap',{x:0.3,y:0.4}))).code,403);
  assert.equal((await call(relay,'POST',route('control/decision'),{allow:true})).json.controlApproved,true);
  let click=await call(relay,'POST',route('control/input'),event('tap',{x:0.3,y:0.4}));
  assert.equal(click.code,202);
  assert.equal((await call(relay,'POST',route('control/input'),event('tap',{x:0.3,y:0.4}))).json.duplicate,true);
  assert.equal((await call(relay,'GET',route('control/poll'))).json.events.length,1);
  assert.equal((await call(relay,'GET',route('control/poll'))).json.events.length,0,
      'event may not replay and click twice');
  now+=150;
  assert.equal((await call(relay,'POST',route('control/input'),
    {...event('right',{x:0.5,y:0.5}),id:b})).code,202);
  assert.equal((await call(relay,'GET',route('control/poll'))).json.events[0].kind,
    'right','right click delivered without replay');
  now+=130;
  assert.equal((await call(relay,'POST',route('control/input'),{...event('key',{key:'ENTER'}),id:c})).code,202);
  now+=4600;
  assert.equal((await call(relay,'GET',route('control/poll'))).json.events.length,0,
      'delayed input must never click after reconnect');
});
test('denial or stop clears control permissions and pointer queue',async()=>{
  let now=100000;
  const relay=new ScreenRelayState(()=>now);
  await call(relay,'POST','/device/v1/screen/view',{device:'phone',session:a});
  await call(relay,'POST',route('control/request'));
  let denied=await call(relay,'POST',route('control/decision'),{allow:false});
  assert.equal(denied.json.controlDenied,true);
  assert.equal((await call(relay,'POST',route('control/request'))).code,403);
  assert.equal((await call(relay,'POST',route('control/input'),event('tap',{x:0,y:0}))).code,403);
  // New viewer is a fresh session and requires fresh Windows approval.
  await call(relay,'POST','/device/v1/screen/stop',{session:a});
  await call(relay,'POST','/device/v1/screen/view',{device:'phone',session:b});
  assert.equal((await call(relay,'GET',route('control/state',b))).json.controlApproved,false);
  await call(relay,'POST',route('control/request',b));
  await call(relay,'POST',route('control/decision',b),{allow:true});
  assert.equal((await call(relay,'POST',route('control/input',b),{...event('tap',{x:0.2,y:0.3}),id:b})).code,202);
  await call(relay,'POST',route('control/disable',b));
  assert.equal((await call(relay,'GET',route('control/poll',b))).json.events.length,0);
  assert.equal((await call(relay,'POST',route('control/input',b),event('tap',{x:0.2,y:0.3}))).code,403);
  await call(relay,'POST','/device/v1/screen/revoke',{session:b});
  assert.equal((await call(relay,'GET',route('control/state',b))).code,403);
});
test('screen expiration automatically revokes input',async()=>{
  let now=100000;
  const relay=new ScreenRelayState(()=>now);
  await call(relay,'POST','/device/v1/screen/view',{device:'phone',session:a});
  await call(relay,'POST',route('control/request'));
  await call(relay,'POST',route('control/decision'),{allow:true});
  now+=8001;
  assert.equal((await call(relay,'POST',route('control/input'),event('key',{key:'ENTER'}))).code,403);
  assert.equal((await call(relay,'GET',route('control/poll'))).code,403);
});
