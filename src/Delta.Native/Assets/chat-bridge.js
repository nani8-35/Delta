function selectTarget(token) {
  const allowed=e=>e && !e.disabled && !e.readOnly && e.getClientRects().length>0 && (e.isContentEditable || e.tagName==='TEXTAREA' || (e.tagName==='INPUT' && ['text','search'].includes(e.type)));
  let focused=document.activeElement;while(focused?.shadowRoot?.activeElement)focused=focused.shadowRoot.activeElement;
  const selector='#prompt-textarea, [contenteditable="true"][role="textbox"], [contenteditable="true"], textarea, input[type="search"]';
  const known=document.getElementById('prompt-textarea');
  let target=allowed(focused)?focused:allowed(known)?known:[...document.querySelectorAll(selector)].find(allowed);
  if(!target){
    // Walk shadow trees only when ordinary editors are absent.
    const roots=[document];
    for(let i=0;i<roots.length&&!target;i++)for(const e of roots[i].querySelectorAll('*'))if(e.shadowRoot){
      target=[...e.shadowRoot.querySelectorAll(selector)].find(allowed);
      if(target)break;roots.push(e.shadowRoot);
    }
  }
  if(!target)return false;
  window.__deltaVoiceTargets ||= new Map();if(window.__deltaVoiceTargets.size>=16)window.__deltaVoiceTargets.delete(window.__deltaVoiceTargets.keys().next().value);window.__deltaVoiceTargets.set(token,target);
  return true;
}
function focusTarget(token) {
  const target=window.__deltaVoiceTargets?.get(token);
  if(!target?.isConnected || target.disabled || target.readOnly || !target.getClientRects().length)return false;
  target.focus();
  if(target.isContentEditable){const range=document.createRange();range.selectNodeContents(target);range.collapse(false);const sel=target.getRootNode().getSelection?.()||window.getSelection();sel.removeAllRanges();sel.addRange(range);}
  else target.setSelectionRange(target.value.length,target.value.length);
  return true;
}
function submitTarget(token){
 const target=window.__deltaVoiceTargets?.get(token);
 if(!target?.isConnected||target.disabled||target.readOnly)return {submitted:false};
 const usable=b=>b&&!b.disabled&&b.getAttribute('aria-disabled')!=='true'&&b.getClientRects().length>0;
 const selector='button[data-testid="send-button"],button[data-testid="submit-button"],button[aria-label="Send prompt"],button[aria-label="Send message"],button[aria-label="Send"],button.send-button';
 const form=target.form||target.closest('form');
 let button=form?.querySelector(selector+',button[type="submit"]'),pending=!!button&&!usable(button);
 if(!usable(button)){let parent=target.parentElement;for(let i=0;parent&&i<7;i++,parent=parent.parentElement){const candidates=[...parent.querySelectorAll(selector)];pending ||= candidates.some(b=>!usable(b));button=candidates.find(usable);if(button)break;}}
 if(usable(button)){button.click();window.__deltaVoiceTargets.delete(token);return {submitted:true,method:'send-button'};}
 if(pending)return {submitted:false,pending:true};
 if(form&&typeof form.requestSubmit==='function'){form.requestSubmit();window.__deltaVoiceTargets.delete(token);return {submitted:true,method:'form'};}
 // Some chat sites expose only an Enter handler. This does not activate the OS window.
 target.focus();const consumed=!target.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',code:'Enter',keyCode:13,which:13,bubbles:true,cancelable:true}));
 target.dispatchEvent(new KeyboardEvent('keyup',{key:'Enter',code:'Enter',keyCode:13,which:13,bubbles:true,cancelable:true}));
 window.__deltaVoiceTargets.delete(token);return {submitted:consumed,method:'enter-handler'};
}
function prepareAttachments(token,names){
 const input=window.__deltaVoiceTargets?.get(token);if(!input)return false;
 let scope=input.form||input.closest('form')||input.parentElement;
 if(!input.form&&!input.closest('form'))for(let i=0;i<4&&scope.parentElement&&scope.parentElement!==document.body;i++)scope=scope.parentElement;
 const selector='img, [data-testid*="attachment"], [data-testid*="file-preview"], [aria-label*="Remove"]';
 window.__deltaAttachmentJobs ||= new Map();
 const nameCounts=names.map(name=>(scope.textContent||'').split(name).length-1);
 window.__deltaAttachmentJobs.set(token,{scope,names,nameCounts,before:new Map([...scope.querySelectorAll(selector)].map(e=>[e,e.getAttribute('src')||e.textContent])),selector});return true;
}
function attachmentReady(token){
 const job=window.__deltaAttachmentJobs?.get(token);if(!job||!job.scope.isConnected)return false;
 const visible=e=>e.getClientRects().length>0;
 if([...job.scope.querySelectorAll('[role="progressbar"], [aria-busy="true"], [data-state="uploading"]')].some(visible))return false;
 const previews=[...job.scope.querySelectorAll(job.selector)].filter(visible).filter(e=>!job.before.has(e)||job.before.get(e)!==(e.getAttribute('src')||e.textContent));
 const text=job.scope.textContent||'';
 return job.names.every((name,i)=>text.split(name).length-1>(job.nameCounts?.[i]||0))||previews.filter(e=>e.tagName==='IMG').length>=job.names.length||previews.filter(e=>e.tagName!=='IMG').length>=job.names.length;
}
function clearJob(token){window.__deltaDrafts?.delete(token);window.__deltaVoiceTargets?.delete(token);window.__deltaAttachmentJobs?.delete(token);}
function attachInPage(images, expectedURL) {
  if (location.href !== expectedURL) return { method: 'page-changed' };
  const roots = [document];
  for (let i = 0; i < roots.length; i++) {
    for (const element of roots[i].querySelectorAll('*')) if (element.shadowRoot) roots.push(element.shadowRoot);
  }
  const findAll = selector => roots.flatMap(root => Array.from(root.querySelectorAll(selector)));
  const visible = element => element.getClientRects().length > 0 && getComputedStyle(element).visibility !== 'hidden';
  const editable = element => !element.disabled && !element.readOnly && visible(element);
  const composers = findAll('#prompt-textarea, [contenteditable="true"], [role="textbox"], textarea').filter(editable);
  let focused = document.activeElement;
  while (focused?.shadowRoot?.activeElement) focused = focused.shadowRoot.activeElement;
  const composer = composers.find(e => e === focused || e.contains(focused)) || composers[0];
  const inputs = findAll('input[type="file"]').filter(input => {
    if (input.disabled || (images.length>1 && !input.multiple)) return false;
    const accept = input.accept.toLowerCase().split(',').map(s => s.trim()).filter(Boolean);
    return !accept.length || accept.some(type => ['image/*', 'image/png', '.png', '*/*'].includes(type));
  });
  const form = composer?.closest('form');
  const input = inputs.find(e => form?.contains(e)) || inputs.find(e => /image|\.png/i.test(e.accept)) || (inputs.length === 1 ? inputs[0] : undefined);
  if (!input && !composer) return { method: 'unsupported' };
  const transfer = new DataTransfer();
  for(const {dataURL,name} of images){
    const binary=atob(dataURL.slice(dataURL.indexOf(',')+1));
    const bytes=Uint8Array.from(binary,char=>char.charCodeAt(0));
    transfer.items.add(new File([bytes],name,{type:'image/png',lastModified:Date.now()}));
  }
  if (input) {
    input.files = transfer.files;
    input.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
    input.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
    return { method: 'file-input' };
  }
  composer.focus();
  const event = new ClipboardEvent('paste', { clipboardData: transfer, bubbles: true, cancelable: true, composed: true });
  const accepted = !composer.dispatchEvent(event);
  return { method: accepted ? 'paste' : 'unsupported' };
}
function voiceText(target) {
 return (target.tagName==='TEXTAREA'||target.tagName==='INPUT')?target.value:target.innerText;
}
function voiceComparable(value) {
 // Rich editors insert paragraph breaks, NBSP and zero-width caret markers.
 return String(value||'').replace(/[\u200b\ufeff]/g,'').replace(/\s+/g,' ').trim();
}
function updateVoiceDraft(token,text) {
 let target=window.__deltaVoiceTargets?.get(token);
 if(!target?.isConnected || target.disabled || target.readOnly) {
  if(!selectTarget(token))return {ok:false,reason:'missing'};
  target=window.__deltaVoiceTargets.get(token);
 }
 window.__deltaDrafts ||= new Map();
 let job=window.__deltaDrafts.get(token);
 if(!job) {const initial=voiceText(target);job={base:initial,last:initial};window.__deltaDrafts.set(token,job);}
 const current=voiceText(target);
 const desired=job.base+(job.base&&!/\s$/.test(job.base)?' ':'')+text;
 if(voiceComparable(current)===voiceComparable(desired)) {job.last=current;return {ok:true};}
 if(voiceComparable(current)!==voiceComparable(job.last) &&
    (job.pending===undefined || voiceComparable(current)!==voiceComparable(job.pending))) return {ok:false,reason:'edited'};
 target.focus();
 if(target.isContentEditable) {
  const range=document.createRange();range.selectNodeContents(target);
  const selection=window.getSelection();selection.removeAllRanges();selection.addRange(range);
 } else target.setSelectionRange(0,target.value.length);
 // Some rich editors report false even though the input event already applied
 // the text. Track the attempted draft before insertion, then trust DOM content.
 job.pending=desired;
 document.execCommand('insertText',false,desired);
 const actual=voiceText(target);
 if(voiceComparable(actual)!==voiceComparable(desired))return {ok:false,reason:'input'};
 job.last=actual;delete job.pending;
 return {ok:true};
}
function voiceDraftReady(token) {
 let target=window.__deltaVoiceTargets?.get(token);const job=window.__deltaDrafts?.get(token);
 if(!target?.isConnected && selectTarget(token))target=window.__deltaVoiceTargets.get(token);
 return !!target && !!job && voiceComparable(voiceText(target)).length>0 &&
  voiceComparable(voiceText(target))===voiceComparable(job.last);
}
