import test from 'node:test';
import assert from 'node:assert/strict';
import {FileRelayState,validFile,MAX_FILE_BYTES} from '../src/file-relay.mjs';
const id='11111111-1111-4111-8111-111111111111';
const body={id,source:'computer',target:'phone',name:'звіт.txt',
  mime:'text/plain',data:Buffer.from('привіт').toString('base64')};
const url=p=>'https://unit.test/device/v1/files/'+p;
test('manual Windows-to-Pixel file is volatile, acknowledged and expires',async()=>{
  let now=100;const state=new FileRelayState(()=>now);
  let post=(p,b)=>new Request(url(p),{method:'POST',
    headers:{'content-type':'application/json'},body:JSON.stringify(b)});
  assert.equal(validFile(body),true);
  assert.equal((await state.fetch(post('send',body))).status,202);
  assert.equal((await state.fetch(post('send',body))).status,409);
  const file=await (await state.fetch(new Request(url('latest')))).json();
  assert.equal(file.name,'звіт.txt');assert.equal(file.data,body.data);
  assert.equal((await state.fetch(post('ack',{id:'22222222-2222-4222-8222-222222222222'}))).status,404);
  assert.equal((await state.fetch(post('ack',{id}))).status,200);
  assert.equal((await (await state.fetch(new Request(url('latest')))).json()).available,false);
  await state.fetch(post('send',body));now+=120_000;
  assert.equal((await (await state.fetch(new Request(url('latest')))).json()).available,false);
});
test('file names, types and oversized payloads are rejected',()=>{
  assert.equal(validFile({...body,name:'../secret.txt'}),false);
  assert.equal(validFile({...body,name:'run\\me.exe'}),false);
  assert.equal(validFile({...body,data:'!!!'}),false);
  assert.equal(validFile({...body,data:Buffer.alloc(MAX_FILE_BYTES+1).toString('base64')}),false);
});
