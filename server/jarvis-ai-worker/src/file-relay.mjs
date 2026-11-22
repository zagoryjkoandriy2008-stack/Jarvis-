/** Single-file, short-lived personal Windows -> Pixel inbox.
 * In-memory Durable Object only: no R2/KV/storage, AI prompts, or logs.
 * Shared bearer is only a prototype, not end-to-end encryption.
 */
const UUID=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const TTL=120_000;
export const MAX_FILE_BYTES=1_048_576;
function json(body,status=200) {
  return new Response(JSON.stringify(body),{status,headers:{
    'content-type':'application/json; charset=utf-8',
    'cache-control':'no-store','x-content-type-options':'nosniff'
  }});
}
async function boundedJson(request,limit) {
  if(!request.body)return null;
  const reader=request.body.getReader();
  const chunks=[];let length=0;
  try {
    for(;;) {
      const next=await reader.read();
      if(next.done)break;
      length+=next.value.byteLength;
      if(length>limit) {
        await reader.cancel('oversized file inbox request');
        return null;
      }
      chunks.push(next.value);
    }
  } finally {try{reader.releaseLock();}catch(_){}}
  const bytes=new Uint8Array(length);let offset=0;
  for(const chunk of chunks){bytes.set(chunk,offset);offset+=chunk.length;}
  try {return JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(bytes));}
  catch(_){return null;}
}
function fileBytes(encoded) {
  if(typeof encoded!=='string' || !encoded.length ||
     encoded.length>1_398_104 || encoded.length%4!==0 ||
     !/^[A-Za-z0-9+/]+={0,2}$/.test(encoded))return -1;
  const extra=encoded.endsWith('==')?2:encoded.endsWith('=')?1:0;
  const size=encoded.length/4*3-extra;
  return size>0&&size<=MAX_FILE_BYTES?size:-1;
}
export function validFile(body) {
  return body && typeof body==='object' && !Array.isArray(body) &&
    UUID.test(body.id||'') &&
    ((body.source==='computer' && body.target==='phone') ||
     (body.source==='phone' && body.target==='computer')) &&
    typeof body.name==='string' &&
    /^[^\\/:*?"<>|\x00-\x1f]{1,100}$/.test(body.name) &&
    body.name!=='.' && body.name!=='..' &&
    typeof body.mime==='string' &&
    /^[a-z0-9.+-]+\/[a-z0-9.+-]{1,80}$/.test(body.mime) &&
    fileBytes(body.data)>0;
}
export class FileRelayState {
  constructor(clock=()=>Date.now()){this.clock=clock;this.files=new Map();}
  active(target){
    const file=this.files.get(target);
    if(file && this.clock()>=file.expires)this.files.delete(target);
    return this.files.get(target)||null;
  }
  async fetch(request) {
    const path=new URL(request.url).pathname;
    const method=request.method;
    const target=new URL(request.url).searchParams.get('target')||'phone';
    if(path==='/device/v1/files/latest' && method==='GET') {
      if(target!=='phone' && target!=='computer')
        return json({error:'Unknown destination'},400);
      const f=this.active(target);
      return json(f?{available:true,id:f.id,name:f.name,mime:f.mime,
        size:f.size,expiresInMs:Math.max(0,f.expires-this.clock()),data:f.data}
        :{available:false});
    }
    if(path==='/device/v1/files/send' && method==='POST') {
      const b=await boundedJson(request,1_500_000);
      if(b==null)return json({error:'Invalid or oversized upload'},413);
      if(!validFile(b))return json({error:'File invalid or exceeds 1 MiB'},400);
      if(this.active(b.target))return json({error:'Device inbox already contains a file'},409);
      this.files.set(b.target,{id:b.id,name:b.name,mime:b.mime,data:b.data,
        size:fileBytes(b.data),expires:this.clock()+TTL});
      return json({id:b.id,accepted:true,expiresInMs:TTL},202);
    }
    if(path==='/device/v1/files/ack' && method==='POST') {
      const b=await boundedJson(request,4096);
      if(b==null)return json({error:'Invalid or oversized ACK'},413);
      const receiver=b?.target||'phone';
      if(receiver!=='phone' && receiver!=='computer')
        return json({error:'Unknown destination'},400);
      const f=this.active(receiver);
      if(!UUID.test(b?.id||'')||!f||f.id!==b.id)
        return json({error:'File unavailable'},404);
      this.files.delete(receiver);
      return json({received:true});
    }
    return json({error:'Not found'},404);
  }
}
