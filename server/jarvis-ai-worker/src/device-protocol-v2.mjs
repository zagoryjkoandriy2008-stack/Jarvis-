/** Version 2 device command contract. Pure validation; never auto-execute. */
const ID=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DEVICE=/^(computer|phone|watch)$/;
const PHASES=new Set(['accepted','running','completed','failed','unknown']);
const KINDS=new Set(['system.status','media.toggle','media.next','media.previous','volume.set',
  'application.open','macro.run','windows.lock','monitor.off','wol.wake','devices.list']);
const EXPIRE_LIMIT=120000;
const DANGEROUS=new Set(['windows.lock','monitor.off','wol.wake']);
const READ=new Set(['system.status','devices.list']);
function plain(x) {return x!==null && typeof x==='object' && !Array.isArray(x) && Object.getPrototypeOf(x)===Object.prototype;}
export function validateDeviceRequest(b,now=Date.now()) {
  if(!plain(b)||b.version!==2||!ID.test(b.requestId||'')||
      !DEVICE.test(b.origin||'')||!DEVICE.test(b.target||'')||b.origin===b.target||
      !KINDS.has(b.kind)||!plain(b.payload)||Object.keys(b.payload).length>8 ||
      !Number.isSafeInteger(b.createdAt)||!Number.isSafeInteger(b.expiresAt)||
      b.createdAt>now+30000||b.createdAt<now-EXPIRE_LIMIT||
      b.expiresAt<=now||b.expiresAt<=b.createdAt||
      b.expiresAt-b.createdAt>EXPIRE_LIMIT||
      typeof b.requiresApproval!=='boolean'||typeof b.replyTo!=='string'||
      !DEVICE.test(b.replyTo)||b.replyTo!==b.origin)return false;
  // Only primitive bounded JSON values are allowed on the command wire.
  if(Object.entries(b.payload).some(([key,value])=>key.length>32||
      !/^[a-zA-Z][a-zA-Z0-9]*$/.test(key)||
      !(typeof value==='boolean'||(typeof value==='number' && Number.isFinite(value))||
      (typeof value==='string' && value.length<=160 && !/[\x00-\x1f]/.test(value)))))return false;
  if(DANGEROUS.has(b.kind) && !b.requiresApproval)return false;
  const expected=b.kind==='volume.set'?'value':
    b.kind==='application.open'?'name':
    b.kind==='macro.run'?'name':null;
  const keys=Object.keys(b.payload);
  if(expected ? keys.length!==1 || keys[0]!==expected : keys.length!==0)return false;
  if(b.kind==='volume.set' && (!Number.isInteger(b.payload.value) || b.payload.value<0||b.payload.value>100))return false;
  if(b.kind==='application.open' && !['notepad','calculator','explorer','browser','spotify'].includes(b.payload.name))return false;
  if(b.kind==='macro.run' && (typeof b.payload.name!=='string'||b.payload.name.length<2))return false;
  return true;
}
export function classifyDeviceAction(kind){
  if(READ.has(kind))return 'readOnly';
  if(DANGEROUS.has(kind))return 'approvalRequired';
  if(KINDS.has(kind))return 'safeAction';
  return 'forbiddenRemote';
}
export function validateDeviceReceipt(b) {
  return plain(b)&&b.version===2&&ID.test(b.requestId||'')&&PHASES.has(b.status)&&
    typeof b.errorCode==='string'&&b.errorCode.length<=60&&
    typeof b.detail==='string'&&b.detail.length<=500&&
    Number.isSafeInteger(b.timestamp);
}
