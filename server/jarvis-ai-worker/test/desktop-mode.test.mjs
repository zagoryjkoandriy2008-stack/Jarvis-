import test from 'node:test';
import assert from 'node:assert/strict';
import {validateInput,createPayload} from '../src/index.mjs';
test('desktop model answers without code interpreter or web tools',()=>{
 const body={message:'{"kind":"openApp","target":"notepad","value":""}',mode:'desktop',history:[]};
 const input=validateInput(body);
 assert.ok(input);
 const payload=createPayload(body,input,{OPENAI_MODEL:'test-model'});
 assert.equal(payload.model,'test-model');
 assert.equal(payload.store,false);
 assert.equal(payload.tools,undefined,'no code executor available to AI desktop');
 assert.equal(payload.max_output_tokens,900);
});
test('desktop vision uses explicit selected image with no automatic tool execution',()=>{
 const img={name:'windows-screen.jpg',mime:'image/jpeg',data:'AAECAw=='};
 const result=validateInput({message:'find text',mode:'desktop',attachment:img});
 assert.ok(result);
 assert.equal(result[0].content[1].type,'input_image');
});
