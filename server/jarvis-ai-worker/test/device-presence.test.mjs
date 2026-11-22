import test from 'node:test';
import assert from 'node:assert/strict';
import {DeviceRelay} from '../src/device-relay.mjs';
function storage() {
 const map=new Map();
 return {map,async get(k){return map.get(k)},async put(k,v){map.set(k,v)},
  async delete(k){map.delete(k)},async list({prefix}){return new Map([...map].filter(([k])=>k.startsWith(prefix)))}};
}
test('device center status never invents online presence',async()=>{
 const st=storage(),relay=new DeviceRelay({storage:st},{});
 async function status(device) {
  const r=await relay.fetch(new Request('https://relay.test/device/v1/status?device='+device));
  return {code:r.status,body:await r.json()};
 }
 let s=await status('watch');
 assert.equal(s.body.online,false);assert.equal(s.body.lastSeenMs,null);
 assert.equal(s.body.pairing,'legacy-shared-token');
 st.map.set('live:phone',Date.now()-500);
 s=await status('phone');
 assert.equal(s.body.online,true);assert.ok(s.body.ageMs>=0);
 st.map.set('live:watch',Date.now()-50000);
 s=await status('watch');
 assert.equal(s.body.online,false);assert.ok(s.body.ageMs>=35000);
 assert.equal((await status('fake')).code,400);
});
