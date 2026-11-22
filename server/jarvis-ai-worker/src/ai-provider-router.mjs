/** Optional multi-provider text/desktop routing.
 * Config comes ONLY from Cloudflare secrets/variables, never user prompts.
 * No fallback on safety refusals, invalid auth, billing or client errors.
 */
const PROVIDERS=new Set(['openai','compatible1','compatible2']);
function endpointFor(id,env) {
  const suffix=id==='compatible1'?'1':'2';
  const endpoint=env['JARVIS_COMPATIBLE_'+suffix+'_ENDPOINT'];
  const model=env['JARVIS_COMPATIBLE_'+suffix+'_MODEL'];
  const key=env['JARVIS_COMPATIBLE_'+suffix+'_API_KEY'];
  const vision=env['JARVIS_COMPATIBLE_'+suffix+'_VISION']==='true';
  if(typeof endpoint!=='string'||typeof model!=='string'||typeof key!=='string'||
     key.length<12||model.length>120||!/^[a-zA-Z0-9._:/-]+$/.test(model))return null;
  let url;
  try{url=new URL(endpoint)}catch{return null}
  if(url.protocol!=='https:' || url.username || url.password || url.search ||
     url.hash || !url.pathname.endsWith('/chat/completions') ||
     !url.hostname.includes('.') || url.hostname.endsWith('.local') ||
     /^\d+\.\d+\.\d+\.\d+$/.test(url.hostname) ||
     url.hostname==='localhost' || url.hostname.endsWith('.internal'))
      return null;
  return {endpoint:url.href,model,key,vision};
}
export function allowedProvider(id) {return PROVIDERS.has(id);}
function compatibleMessages(input,instructions) {
  const messages=[{role:'system',content:instructions}];
  for(const entry of input) {
    if(typeof entry.content==='string') {
      messages.push({role:entry.role,content:entry.content});continue;
    }
    if(!Array.isArray(entry.content) || entry.role!=='user')return null;
    const parts=[];
    for(const chunk of entry.content) {
      if(chunk.type==='input_text')parts.push({type:'text',text:chunk.text});
      else if(chunk.type==='input_image')parts.push({type:'image_url',
        image_url:{url:chunk.image_url}});
      else return null; // no file interpretation on compatible API
    }
    messages.push({role:'user',content:parts});
  }
  return messages;
}
async function readFailure(response) {
  let code='';
  try {
    const body=await response.json();
    code=typeof body?.error?.code==='string'?body.error.code:'';
  }catch{/* never expose raw upstream response */}
  const retry=response.status>=500 ||
    (response.status===429 && code!=='insufficient_quota' &&
      code!=='billing_hard_limit_reached');
  return {ok:false,status:response.status,retry};
}
async function perform(id,{env,input,body,openaiPayload,fetchImpl,extractText}) {
  if(id==='openai') {
    if(!env.OPENAI_API_KEY)return {ok:false,status:503,retry:true};
    let res;
    try {res=await fetchImpl('https://api.openai.com/v1/responses',{
      method:'POST',headers:{authorization:'Bearer '+env.OPENAI_API_KEY,
        'content-type':'application/json'},body:JSON.stringify(openaiPayload)
    });}catch{return {ok:false,status:502,retry:true}}
    if(!res.ok)return readFailure(res);
    try {
      const data=await res.json(),reply=extractText(data?.output);
      return reply?{ok:true,reply,provider:'openai'}:{ok:false,status:502,retry:true};
    }catch{return {ok:false,status:502,retry:true}}
  }
  const config=endpointFor(id,env);
  if(!config)return {ok:false,status:503,retry:true};
  if(body.attachment && !config.vision)return {ok:false,status:503,retry:true};
  const messages=compatibleMessages(input,openaiPayload.instructions);
  if(!messages)return {ok:false,status:400,retry:false};
  let res;
  try {res=await fetchImpl(config.endpoint,{
    method:'POST',headers:{authorization:'Bearer '+config.key,
      'content-type':'application/json'},
    body:JSON.stringify({model:config.model,messages,
      max_tokens:body.mode==='desktop'?900:2000,temperature:0.2})
  });}catch{return {ok:false,status:502,retry:true}}
  if(!res.ok)return readFailure(res);
  try {
    const data=await res.json();
    const reply=data?.choices?.[0]?.message?.content;
    if(typeof reply!=='string'||!reply.trim()||reply.length>12000)
      return {ok:false,status:502,retry:true};
    return {ok:true,reply:reply.trim(),provider:id};
  }catch{return {ok:false,status:502,retry:true}}
}
export async function routeProviders(args) {
  const primary=args.env.JARVIS_AI_PRIMARY||'openai';
  const backup=args.env.JARVIS_AI_FALLBACK||'';
  if(!allowedProvider(primary) || (backup && !allowedProvider(backup)))
    return {ok:false,status:503,retry:false};
  const initial=await perform(primary,args);
  if(initial.ok||!initial.retry||!backup||backup===primary)return initial;
  return perform(backup,args);
}
