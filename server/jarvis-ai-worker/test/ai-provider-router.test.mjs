import test from 'node:test';
import assert from 'node:assert/strict';
import {routeProviders} from '../src/ai-provider-router.mjs';
const input=[{role:'user',content:'say hello'}];
const openaiPayload={model:'primary',instructions:'Reply Ukrainian',input};
const extractText=output=>output?.[0]?.content?.[0]?.text||'';
const base={body:{mode:'desktop'},input,openaiPayload,extractText};
const goodCompat={
  JARVIS_COMPATIBLE_1_API_KEY:'safe-test-key-123456',
  JARVIS_COMPATIBLE_1_ENDPOINT:'https://example.ai/v1/chat/completions',
  JARVIS_COMPATIBLE_1_MODEL:'safe-chat',
};
test('multi-provider fallback only for outages, never 403 policy refusal',async()=>{
 let calls=[];
 let env={OPENAI_API_KEY:'test-openai-key',...goodCompat,
    JARVIS_AI_PRIMARY:'openai',JARVIS_AI_FALLBACK:'compatible1'};
 const fetchImpl=async url=>{
   calls.push(url);
   return url.includes('openai.com')?new Response('{}',{status:503})
     :new Response(JSON.stringify({choices:[{message:{content:'{"kind":"finish","value":"ok"}'}}]}));
 };
 const yes=await routeProviders({...base,env,fetchImpl});
 assert.equal(yes.provider,'compatible1');assert.equal(calls.length,2);
 calls=[];
 const no=await routeProviders({...base,env,fetchImpl:async url=>{
   calls.push(url);return new Response('{}',{status:403});
 }});
 assert.equal(no.ok,false);assert.equal(calls.length,1);
});
test('model router rejects private IP and unsafe endpoints',async()=>{
 const bad={...goodCompat,JARVIS_COMPATIBLE_1_ENDPOINT:'https://127.0.0.1/v1/chat/completions',
   JARVIS_AI_PRIMARY:'compatible1'};
 let calls=0;
 const v=await routeProviders({...base,env:bad,fetchImpl:()=>{calls++;throw Error('should not send')}});
 assert.equal(v.ok,false);assert.equal(calls,0);
});
test('vision never silently forwards screenshots to text-only fallback',async()=>{
 const env={...goodCompat,JARVIS_AI_PRIMARY:'compatible1'};
 const v=await routeProviders({...base,env,body:{mode:'desktop',attachment:{mime:'image/jpeg'}},
  fetchImpl:()=>{throw Error('bad')}});
 assert.equal(v.ok,false);
});

test('compatible-only provider uses Cloudflare-held secret with no OpenAI key',async()=>{
  const env={...goodCompat,JARVIS_AI_PRIMARY:'compatible1'};
  const fetchImpl=async url=>{
    assert.equal(url,'https://example.ai/v1/chat/completions');
    return new Response(JSON.stringify({choices:[{message:{content:'ok'}}]}));
  };
  const r=await routeProviders({...base,env,fetchImpl});
  assert.equal(r.ok,true);assert.equal(r.reply,'ok');
});
