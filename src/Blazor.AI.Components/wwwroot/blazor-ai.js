// Blazor.AI browser helpers.
//
// Everything here is behaviour the browser will not give a .NET component for free: scroll
// position, textarea measurement, the clipboard, file drops, and speech recognition. Each helper is
// written so that failing gracefully is the worst case, because a chat UI must keep working when a
// permission is denied or an API is missing.

const scrollObservers = new WeakMap();
const pasteHandlers = new WeakMap();
const dropHandlers = new WeakMap();
let recognition = null;

/**
 * Watches a scroll container and reports whether the user is pinned to the bottom.
 * Auto-scroll follows new content only while pinned, so scrolling up to read is never interrupted.
 */
export function observeScroll(element, dotnet, threshold = 48) {
  if (!element) return;
  stopObservingScroll(element);

  let pinned = true;
  let frame = 0;

  const report = () => {
    frame = 0;
    const distance = element.scrollHeight - element.scrollTop - element.clientHeight;
    const next = distance <= threshold;
    if (next !== pinned) {
      pinned = next;
      dotnet.invokeMethodAsync('OnPinnedChanged', pinned).catch(() => {});
    }
  };

  const onScroll = () => {
    if (frame) return;
    frame = requestAnimationFrame(report);
  };

  element.addEventListener('scroll', onScroll, { passive: true });

  const resize = new ResizeObserver(() => {
    if (pinned) element.scrollTop = element.scrollHeight;
  });
  resize.observe(element);

  scrollObservers.set(element, { onScroll, resize });
  report();
}

export function stopObservingScroll(element) {
  if (!element) return;
  const state = scrollObservers.get(element);
  if (!state) return;
  element.removeEventListener('scroll', state.onScroll);
  state.resize.disconnect();
  scrollObservers.delete(element);
}

export function scrollToBottom(element, smooth = false) {
  if (!element) return;
  // An explicit behaviour overrides the stylesheet, so reduced motion has to be honoured here too.
  const animate = smooth && !prefersReducedMotion();
  element.scrollTo({ top: element.scrollHeight, behavior: animate ? 'smooth' : 'auto' });
}

export function isAtBottom(element, threshold = 48) {
  if (!element) return true;
  return element.scrollHeight - element.scrollTop - element.clientHeight <= threshold;
}

/** Grows a textarea to fit its content, up to a row cap, then lets it scroll. */
export function autoResize(textarea, maxRows = 12) {
  if (!textarea) return 0;
  const style = window.getComputedStyle(textarea);
  const lineHeight = parseFloat(style.lineHeight) || 20;
  const padding = parseFloat(style.paddingTop) + parseFloat(style.paddingBottom);
  const border = parseFloat(style.borderTopWidth) + parseFloat(style.borderBottomWidth);

  textarea.style.height = 'auto';
  const max = lineHeight * maxRows + padding + border;
  const height = Math.min(textarea.scrollHeight + border, max);
  textarea.style.height = `${height}px`;
  textarea.style.overflowY = textarea.scrollHeight + border > max ? 'auto' : 'hidden';
  return height;
}

export async function copyText(text) {
  try {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    // Fall through to the legacy path below.
  }

  try {
    const area = document.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    const ok = document.execCommand('copy');
    document.body.removeChild(area);
    return ok;
  } catch {
    return false;
  }
}

export function focusElement(element, selectAll = false) {
  if (!element) return;
  element.focus({ preventScroll: true });
  if (selectAll && typeof element.select === 'function') element.select();
}

export function downloadText(fileName, text, mime = 'text/plain') {
  const blob = new Blob([text], { type: mime });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/**
 * Reports images pasted into the composer, so a screenshot on the clipboard becomes an attachment.
 * Text pastes are left alone for the browser to handle.
 */
export function registerPaste(element, dotnet) {
  if (!element) return;
  unregisterPaste(element);

  const handler = async (event) => {
    const items = Array.from(event.clipboardData?.items ?? []);
    const files = items.filter((i) => i.kind === 'file').map((i) => i.getAsFile()).filter(Boolean);
    if (files.length === 0) return;

    event.preventDefault();
    for (const file of files) {
      const buffer = await file.arrayBuffer();
      await dotnet.invokeMethodAsync('OnPastedFile', {
        name: file.name || 'pasted-image.png',
        contentType: file.type || 'application/octet-stream',
        size: file.size,
        data: bytesToBase64(new Uint8Array(buffer)),
      }).catch(() => {});
    }
  };

  element.addEventListener('paste', handler);
  pasteHandlers.set(element, handler);
}

export function unregisterPaste(element) {
  const handler = pasteHandlers.get(element);
  if (!handler) return;
  element.removeEventListener('paste', handler);
  pasteHandlers.delete(element);
}

/** Reports files dropped onto an element, and toggles a class while a drag is over it. */
export function registerDrop(element, dotnet) {
  if (!element) return;
  unregisterDrop(element);

  let depth = 0;

  const setActive = (active) => {
    element.classList.toggle('bai-dropping', active);
    dotnet.invokeMethodAsync('OnDragStateChanged', active).catch(() => {});
  };

  const onEnter = (event) => {
    if (!event.dataTransfer?.types?.includes('Files')) return;
    event.preventDefault();
    depth++;
    if (depth === 1) setActive(true);
  };

  const onOver = (event) => {
    if (event.dataTransfer?.types?.includes('Files')) event.preventDefault();
  };

  const onLeave = () => {
    depth = Math.max(0, depth - 1);
    if (depth === 0) setActive(false);
  };

  const onDrop = async (event) => {
    if (!event.dataTransfer?.files?.length) return;
    event.preventDefault();
    depth = 0;
    setActive(false);

    for (const file of Array.from(event.dataTransfer.files)) {
      const buffer = await file.arrayBuffer();
      await dotnet.invokeMethodAsync('OnDroppedFile', {
        name: file.name,
        contentType: file.type || 'application/octet-stream',
        size: file.size,
        data: bytesToBase64(new Uint8Array(buffer)),
      }).catch(() => {});
    }
  };

  element.addEventListener('dragenter', onEnter);
  element.addEventListener('dragover', onOver);
  element.addEventListener('dragleave', onLeave);
  element.addEventListener('drop', onDrop);
  dropHandlers.set(element, { onEnter, onOver, onLeave, onDrop });
}

export function unregisterDrop(element) {
  const handlers = dropHandlers.get(element);
  if (!handlers) return;
  element.removeEventListener('dragenter', handlers.onEnter);
  element.removeEventListener('dragover', handlers.onOver);
  element.removeEventListener('dragleave', handlers.onLeave);
  element.removeEventListener('drop', handlers.onDrop);
  dropHandlers.delete(element);
}

/** Whether this browser can transcribe speech without a server round trip. */
export function isDictationSupported() {
  return Boolean(window.SpeechRecognition || window.webkitSpeechRecognition);
}

export function startDictation(dotnet, language) {
  const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!Recognition) return false;

  stopDictation();
  const current = new Recognition();
  recognition = current;
  current.lang = language || navigator.language || 'en-US';
  current.continuous = true;
  current.interimResults = true;

  current.onresult = (event) => {
    let final = '';
    let interim = '';
    for (let i = event.resultIndex; i < event.results.length; i++) {
      const result = event.results[i];
      if (result.isFinal) final += result[0].transcript;
      else interim += result[0].transcript;
    }
    dotnet.invokeMethodAsync('OnTranscriptReceived', final, interim).catch(() => {});
  };

  current.onerror = (event) => {
    if (recognition !== current) return;
    dotnet.invokeMethodAsync('OnDictationError', String(event.error ?? 'unknown')).catch(() => {});
  };

  // A recogniser that was stopped or replaced reports its end late; only the live one may tell
  // .NET that dictation is over, or a quick stop-and-restart would show the button as off.
  current.onend = () => {
    if (recognition !== current) return;
    recognition = null;
    dotnet.invokeMethodAsync('OnDictationEnded').catch(() => {});
  };

  try {
    current.start();
  } catch {
    recognition = null;
    return false;
  }

  return true;
}

export function stopDictation() {
  if (!recognition) return;
  try {
    recognition.stop();
  } catch {
    // Already stopped.
  }
  recognition = null;
}

function bytesToBase64(bytes) {
  let binary = '';
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
  }
  return btoa(binary);
}

export function prefersReducedMotion() {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

const menuKeys = ['ArrowDown', 'ArrowUp', 'Enter', 'Tab', 'Escape'];

/**
 * Stops Enter from inserting a newline when Enter is the submit key, and stops the keys that drive
 * the command and mention menu from also moving the caret or focus while that menu is open.
 *
 * Blazor cannot decide preventDefault per keystroke, so the decision is made here where the event
 * is: Enter alone is swallowed, Enter with any modifier is left to the browser. The menu marks the
 * textarea with data-bai-menu-open while it is showing.
 */
export function registerSubmitKey(element, submitOnEnter) {
  if (!element) return;
  const existing = submitKeyHandlers.get(element);
  if (existing) element.removeEventListener('keydown', existing);
  submitKeyHandlers.delete(element);

  const handler = (event) => {
    if (event.isComposing) return;
    if (element.hasAttribute('data-bai-menu-open') && menuKeys.includes(event.key)) {
      event.preventDefault();
      return;
    }

    if (!submitOnEnter) return;
    if (event.key !== 'Enter') return;
    if (event.shiftKey || event.altKey || event.ctrlKey || event.metaKey) return;
    if (event.isComposing) return;
    event.preventDefault();
  };

  element.addEventListener('keydown', handler);
  submitKeyHandlers.set(element, handler);
}

const submitKeyHandlers = new WeakMap();
