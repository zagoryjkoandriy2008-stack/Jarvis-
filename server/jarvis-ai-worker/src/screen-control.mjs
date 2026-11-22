/** Ephemeral, view-session-scoped remote PC input. No Durable Object
 * storage, database, file logging or AI execution. A Windows user must
 * explicitly approve EACH active viewer session in their local WPF UI.
 * All clients must still pass the Cloudflare Worker bearer gate.
 */
const UUID=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const ACCEPTED_KEYS=new Set([
  'ENTER','ESC','TAB','BACKSPACE','DELETE','SPACE',
  'UP','DOWN','LEFT','RIGHT','HOME','END','PAGEUP','PAGEDOWN',
  'CTRL_A','CTRL_C','CTRL_V'
]);
const ACTIONS=new Set(['tap','double','right','drag','scroll','text','key']);
const INPUT_TTL=4500;
const MAX_PENDING=24;
function response(body,status=200) {
  return new Response(JSON.stringify(body),{status,headers:{
    'content-type':'application/json; charset=utf-8','cache-control':'no-store'
  }});
}
function norm(x){return typeof x==='number' && Number.isFinite(x) && x>=0 && x<=1;}
export function validScreenInput(item) {
  if(!item||typeof item!=='object'||Array.isArray(item) ||
     !UUID.test(item.id||'')||!ACTIONS.has(item.kind))return false;
  switch(item.kind) {
    case 'tap':case 'double':case 'right':
      return norm(item.x)&&norm(item.y);
    case 'drag':
      return norm(item.x)&&norm(item.y)&&norm(item.x2)&&norm(item.y2);
    case 'scroll':
      return Number.isInteger(item.delta)&&
        item.delta>=-8&&item.delta<=8&&item.delta!==0;
    case 'text':
      return typeof item.text==='string' && item.text.length>0&&
        item.text.length<=160 &&
        !/[\u0000-\u001f\u007f]/.test(item.text);
    case 'key':return ACCEPTED_KEYS.has(item.key);
    default:return false;
  }
}
export class ScreenControlState {
  constructor(clock=()=>Date.now()) {
    this.clock=clock;
    this.reset();
  }
  reset() {
    this.requested=false;this.approved=false;this.denied=false;
    this.commands=[];this.ids=new Map();
    this.lastInput=0;
  }
  status() {
    return {controlRequested:this.requested&&!this.denied&&!this.approved,
      controlApproved:this.approved,
      controlDenied:this.denied};
  }
  expireIds(now) {
    for(const [id,at]of this.ids)
      if(now-at>12000)this.ids.delete(id);
    this.commands=this.commands.filter(x=>now-x.time<INPUT_TTL);
  }
  async fetch(request,session,activeSession) {
    const url=new URL(request.url),path=url.pathname;
    const method=request.method;
    const now=this.clock();
    if(!session || session!==activeSession)
      return response({error:'No active screen viewer'},403);
    if(path==='/device/v1/screen/control/request' && method==='POST') {
      if(this.denied)return response({error:'Windows denied remote control for this session'},403);
      if(!this.approved)this.requested=true;
      return response(this.status());
    }
    if(path==='/device/v1/screen/control/decision' && method==='POST') {
      let body;
      try {body=await request.json();}catch(_) {return response({error:'Invalid decision'},400);}
      if(typeof body?.allow!=='boolean'||!this.requested)
        return response({error:'No pending control request'},409);
      if(this.denied)return response({error:'Already denied'},403);
      this.approved=body.allow;
      this.denied=!body.allow;
      this.requested=false;
      if(!body.allow)this.commands=[];
      return response(this.status());
    }
    if(path==='/device/v1/screen/control/disable' && method==='POST') {
      // Window owner revokes. Local receiver independently refuses input
      // after revocation; spoofing this response cannot enable local input.
      this.denied=true;this.approved=false;this.requested=false;
      this.commands=[];
      return response(this.status());
    }
    if(path==='/device/v1/screen/control/state' && method==='GET')
      return response(this.status());
    if(path==='/device/v1/screen/control/input' && method==='POST') {
      if(!this.approved||this.denied)
        return response({error:'Windows has not approved remote input'},403);
      let body;
      try {body=await request.json();}catch(_) {return response({error:'Invalid input'},400);}
      if(!validScreenInput(body))
        return response({error:'Rejected remote key or pointer event'},400);
      this.expireIds(now);
      if(this.ids.has(body.id))return response({accepted:true,duplicate:true});
      if(this.commands.length>=MAX_PENDING)
        return response({error:'Input queue full'},429);
      if(now-this.lastInput<75)return response({error:'Input rate limited'},429);
      this.lastInput=now;
      this.ids.set(body.id,now);
      this.commands.push({id:body.id,kind:body.kind,
        x:body.x,y:body.y,x2:body.x2,y2:body.y2,
        delta:body.delta,key:body.key,text:body.text,time:now});
      return response({accepted:true},202);
    }
    if(path==='/device/v1/screen/control/poll' && method==='GET') {
      if(!this.approved||this.denied)return response({events:[]});
      this.expireIds(now);
      const result=this.commands.splice(0,8).map(({time,...event})=>event);
      // Remove once delivered. A connection failure may lose an event but
      // must never duplicate a click, especially for purchasing/payment.
      return response({events:result});
    }
    return response({error:'Unknown control endpoint'},404);
  }
}
