/** Privacy-first screen snapshots for a single personal JARVIS installation.
 * Frames are transient process memory inside the Durable Object, NEVER KV/R2,
 * Durable Object storage, AI prompts, logs or files. The Windows user must
 * also explicitly enable screen sharing; this endpoint cannot start capture.
 * Phone viewer heartbeats end a session after ~8s without refresh.
 */
const UUID=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const VIEW_TTL_MS=8_000;
const FRAME_TTL_MS=5_000;
const MAX_JPEG_BYTES=240_000;
const MIN_FRAME_MS=500;
function json(data,status=200) {
  return new Response(JSON.stringify(data),{status,headers:{
    'content-type':'application/json; charset=utf-8',
    'cache-control':'no-store','x-content-type-options':'nosniff'
  }});
}

async function boundedBytes(request,maxBytes) {
  if(!request.body)return null;
  const declared=Number(request.headers.get('content-length'));
  if(Number.isFinite(declared) && declared>maxBytes)return null;
  const reader=request.body.getReader();
  const buffers=[];
  let total=0;
  try {
    for(;;) {
      const next=await reader.read();
      if(next.done)break;
      total+=next.value.byteLength;
      if(total>maxBytes) {
        await reader.cancel('oversized screen frame');
        return null;
      }
      buffers.push(next.value);
    }
  } finally {
    try {reader.releaseLock();}catch(_){}
  }
  const bytes=new Uint8Array(total);
  let offset=0;
  for(const b of buffers) {
    bytes.set(b,offset);offset+=b.byteLength;
  }
  return bytes;
}

export class ScreenRelayState {
  constructor(clock=()=>Date.now()) {
    this.clock=clock;
    this.session=null;
    this.viewerLastSeen=0;
    this.frame=null;
    this.frameTime=0;
    this.previousUpload=0;
  }
  active() {
    if(!this.session)return false;
    const active=this.clock()-this.viewerLastSeen<=VIEW_TTL_MS;
    if(!active)this.reset();
    return active;
  }
  reset() {
    this.session=null;this.viewerLastSeen=0;
    this.frame=null;this.frameTime=0;this.previousUpload=0;
  }
  async fetch(request) {
    const url=new URL(request.url);
    const path=url.pathname;
    const method=request.method;
    const now=this.clock();
    const session=url.searchParams.get('session');
    if(path==='/device/v1/screen/view' && method==='POST') {
      let body;
      try {body=await request.json();}
      catch(_) {return json({error:'Invalid viewer request'},400);}
      if(body?.device!=='phone' || !UUID.test(body?.session||''))
        return json({error:'Invalid viewer identity'},400);
      if(this.active() && this.session!==body.session)
        return json({error:'Screen already has a viewer'},409);
      if(this.session!==body.session) {
        this.reset();
        this.session=body.session;
      }
      this.viewerLastSeen=now;
      return json({active:true,expiresInMs:VIEW_TTL_MS});
    }
    if(path==='/device/v1/screen/stop' && method==='POST') {
      let body;
      try {body=await request.json();}
      catch(_) {return json({error:'Invalid stop request'},400);}
      if(!UUID.test(body?.session||''))
        return json({error:'Invalid viewer session'},400);
      if(body.session===this.session)this.reset();
      return json({active:false});
    }
    if(path==='/device/v1/screen/state' && method==='GET') {
      return json(this.active()
        ?{active:true,session:this.session}
        :{active:false});
    }
    if(path==='/device/v1/screen/frame') {
      if(!UUID.test(session||'') || !this.active() ||
         session!==this.session)
        return json({error:'Viewer not active'},403);
      if(method==='POST') {
        if(request.headers.get('content-type')!=='image/jpeg')
          return json({error:'JPEG required'},415);
        // Prevent a single client/token from flooding Cloudflare.
        if(now-this.previousUpload<MIN_FRAME_MS)
          return json({error:'Too many frames'},429);
        const jpeg=await boundedBytes(request,MAX_JPEG_BYTES);
        if(!jpeg || jpeg.length<20 ||
           jpeg[0]!==0xff || jpeg[1]!==0xd8 ||
           jpeg[jpeg.length-2]!==0xff || jpeg[jpeg.length-1]!==0xd9)
          return json({error:'Invalid or oversized JPEG'},413);
        if(!this.active() || session!==this.session)
          return json({error:'Viewer disconnected'},409);
        this.frame=jpeg;
        this.frameTime=this.clock();
        this.previousUpload=this.frameTime;
        return json({accepted:true,size:jpeg.length},202);
      }
      if(method==='GET') {
        if(!this.frame || now-this.frameTime>FRAME_TTL_MS)
          return json({error:'Frame unavailable'},404);
        return new Response(this.frame.slice(),{status:200,headers:{
          'content-type':'image/jpeg','content-length':String(this.frame.length),
          'cache-control':'no-store, max-age=0','pragma':'no-cache',
          'x-content-type-options':'nosniff',
          'content-security-policy':"default-src 'none'",
          'x-jarvis-frame-time':String(this.frameTime)
        }});
      }
    }
    return json({error:'Unknown screen endpoint'},404);
  }
}
