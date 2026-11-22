import test from 'node:test';
import assert from 'node:assert/strict';
import {classifyDeviceAction,validateDeviceRequest} from '../src/device-protocol-v2.mjs';
test('remote screen and lock actions require owner approval',()=>{
  for(const name of ['windows.lock','monitor.off','wol.wake'])
    assert.equal(classifyDeviceAction(name),'approvalRequired');
  for(const name of ['screen.capture','password.read','shell.run','ai.desktop'])
    assert.equal(classifyDeviceAction(name),'forbiddenRemote');
});
