import test from 'node:test';
import assert from 'node:assert/strict';
import {DeviceRelay,validEnvelope,validAck} from '../src/device-relay.mjs';

const id='35c8d264-bca6-4a33-9155-84224d2fef01';
const sample={id,origin:'computer',target:'phone',command:'витратив 150 готівка транспорт автобус'};
function storage() {
  const map=new Map();
  return {
    map,
    async get(key){return map.get(key)},
    async put(key,value){map.set(key,value)},
    async delete(key){map.delete(key)},
    async list({prefix}){return new Map([...map].filter(([k])=>k.startsWith(prefix)))}
  };
}
async function call(relay,method,path,body) {
  const response=await relay.fetch(new Request('https://worker.test'+path,{
    method,headers:body?{'content-type':'application/json'}:{},
    body:body?JSON.stringify(body):undefined
  }));
  return {status:response.status,json:await response.json()};
}
test('valid envelopes allow only user-typed bounded commands',()=>{
  assert.ok(validEnvelope(sample));
  assert.ok(validEnvelope({...sample,origin:'watch',target:'computer'}));
  for(const mutation of [
    {origin:'unknown'},{target:'unknown'},{target:'computer'},
    {command:''},{command:'x'.repeat(251)},
    {command:'write\npassword'},{command:'password 1234'},
    {command:'API key 123'},{id:'not-uuid'}
  ]) assert.equal(validEnvelope({...sample,...mutation}),false);
  assert.ok(validAck({id,device:'phone',ok:true,detail:'Готово'}));
  assert.equal(validAck({id,device:'computer',ok:true,detail:'x'.repeat(1601)}),false);
});
test('queue, poll, ack, deduplicate and query result',async()=>{
  const state={storage:storage()},relay=new DeviceRelay(state,{});
  let result=await call(relay,'POST','/device/v1/send',sample);
  assert.equal(result.status,202);
  assert.equal((await call(relay,'GET','/device/v1/online?device=phone')).json.online,false);
  result=await call(relay,'GET','/device/v1/poll?device=phone');
  assert.equal(result.json.items.length,1);
  assert.equal(result.json.items[0].command,sample.command);
  assert.equal((await call(relay,'GET','/device/v1/online?device=phone')).json.online,true);
  assert.equal((await call(relay,'GET','/device/v1/poll?device=computer')).json.items.length,0);
  assert.equal((await call(relay,'GET','/device/v1/poll?device=phone')).json.items.length,0);
  assert.equal((await call(relay,'GET',`/device/v1/result?id=${id}&origin=computer`)).json.done,false);
  assert.equal((await call(relay,'GET',`/device/v1/result?id=${id}&origin=watch`)).status,404);
  assert.equal((await call(relay,'POST','/device/v1/ack',
      {id,device:'computer',ok:true,detail:'ok'})).status,403);
  assert.equal((await call(relay,'POST','/device/v1/ack',
      {id,device:'phone',ok:true,detail:'Збережено'})).json.state,'done');
  assert.equal((await call(relay,'POST','/device/v1/ack',
      {id,device:'phone',ok:false,detail:'Wrong'})).json.state,'done');
  const finish=await call(relay,'GET',`/device/v1/result?id=${id}&origin=computer`);
  assert.equal(finish.json.ok,true);
  assert.equal(finish.json.detail,'Збережено');
  assert.equal((await call(relay,'POST','/device/v1/send',sample)).status,200);
  assert.equal((await call(relay,'POST','/device/v1/send',
    {...sample,command:'інша команда'})).status,409);
});
test('expired commands are never executed',async()=>{
  const state={storage:storage()},relay=new DeviceRelay(state,{});
  await call(relay,'POST','/device/v1/send',sample);
  state.storage.map.get('m:'+id).created=Date.now()-130000;
  assert.equal((await call(relay,'GET','/device/v1/poll?device=phone')).json.items.length,0);
  assert.equal((await call(relay,'GET',`/device/v1/result?id=${id}&origin=computer`)).status,404);
});
test('workers reject invalid JSON',async()=>{
  const relay=new DeviceRelay({storage:storage()},{});
  const response=await relay.fetch(new Request('https://worker.test/device/v1/send',
      {method:'POST',body:'not JSON'}));
  assert.equal(response.status,400);
});
