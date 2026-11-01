(function(action, text) {
  if (action === 'copy') return window.getSelection()?.toString() || '';
  const usable = e => e?.isConnected && !e.disabled && !e.readOnly && e.getClientRects().length;
  const target = usable(document.activeElement) && document.activeElement.matches('textarea,input[type="text"],input[type="search"],[contenteditable="true"]')
    ? document.activeElement : [...document.querySelectorAll('#prompt-textarea,textarea,[contenteditable="true"][role="textbox"],[contenteditable="true"],input[type="search"]')].find(usable);
  if (!target) return false;
  if (action === 'insert') {
    target.focus();
    if (target.isContentEditable) return document.execCommand('insertText', false, text);
    const start = target.selectionStart ?? target.value.length;
    const end = target.selectionEnd ?? start;
    const setter = Object.getOwnPropertyDescriptor(target.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype, 'value').set;
    setter.call(target, target.value.slice(0,start) + text + target.value.slice(end));
    target.setSelectionRange(start+text.length,start+text.length);
    target.dispatchEvent(new Event('input',{bubbles:true}));
    target.dispatchEvent(new Event('change',{bubbles:true}));
    return true;
  }
  if (action === 'submit') {
    const form = target.form || target.closest('form');
    const selector = 'button[data-testid="send-button"],button[aria-label="Send prompt"],button[aria-label="Send message"],button[aria-label="Send"]';
    const button = [...document.querySelectorAll(selector)].find(usable) || [...(form?.querySelectorAll('button[type="submit"]') || [])].find(usable);
    if (button) { button.click(); return true; }
    if (form?.requestSubmit && form.checkValidity()) { form.requestSubmit(); return true; }
    const accepted = !target.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',code:'Enter',bubbles:true,cancelable:true}));
    target.dispatchEvent(new KeyboardEvent('keyup',{key:'Enter',code:'Enter',bubbles:true}));
    return accepted;
  }
  return false;
})
