import test from 'node:test';
import assert from 'node:assert/strict';
import {validateDeviceRequest,validateDeviceReceipt,classifyDeviceAction} from '../src/device-protocol-v2.mjs';
const now=1730000000000;
const id='35c8d264-bca6-4a33-9155-84224d2fef01';
const request={version:2,requestId:id,origin:'watch',target:'computer',kind:'system.status',
  payload:{},createdAt:now-1000,expiresAt:now+30000,requiresApproval:false,replyTo:'watch'};
test('v2 contract validates TTL, roles, identifiers and forbidden payloads',()=>{
  assert.equal(validateDeviceRequest(request,now),true);
  for(const v of [{origin:'computer'},{expiresAt:now-1},{expiresAt:now+200000},
    {createdAt:now-200000},{replyTo:'phone'},{kind:'shell.run'},
    {payload:{code:'powershell.exe'}},{payload:{notes:'x'.repeat(161)}},{version:1}])
    assert.equal(validateDeviceRequest({...request,...v},now),false,JSON.stringify(v));
});
test('v2 action permissions always require approval for risky actions',()=>{
  assert.equal(classifyDeviceAction('wol.wake'),'approvalRequired');
  assert.equal(classifyDeviceAction('system.status'),'readOnly');
  assert.equal(classifyDeviceAction('shell.run'),'forbiddenRemote');
  assert.equal(validateDeviceRequest({...request,kind:'wol.wake'},now),false);
  assert.equal(validateDeviceRequest({...request,kind:'wol.wake',requiresApproval:true},now),true);
  assert.equal(validateDeviceRequest({...request,kind:'volume.set',payload:{value:101}},now),false);
});
test('receipt status distinguishes queueing from real completion',()=>{
  const receipt={version:2,requestId:id,status:'accepted',errorCode:'',detail:'',timestamp:now};
  assert.ok(validateDeviceReceipt(receipt));
  assert.ok(validateDeviceReceipt({...receipt,status:'unknown'}));
  assert.equal(validateDeviceReceipt({...receipt,status:'success'}),false);
});
