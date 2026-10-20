// Blazor.AI browser helpers.
//
// Everything here is behaviour the browser will not give a .NET component for free: scroll
// position, textarea measurement, the clipboard, file drops, and speech recognition. Each helper is
// written so that failing gracefully is the worst case, because a chat UI must keep working when a
// permission is denied or an API is missing.
//
// Every helper that attaches a listener or an observer returns a numeric handle and records a
// release function against the element, so the registration can be undone either way: by handle
// through disposeHandle, or by element through the matching unregister function. Nothing here
// outlives the element it was attached to without .NET asking for it.

const scrollObservers = new WeakMap();
const pasteHandlers = new WeakMap();
const dropHandlers = new WeakMap();
const submitKeyHandlers = new WeakMap();
const handles = new Map();
const previewUrls = new Set();
let nextHandle = 1;
let recognition = null;
let recognitionHandle = 0;

/** Records a release function for an element, under a handle .NET can hold on to. */
function track(map, element, dispose) {
  const id = nextHandle++;
  const release = () => {
    if (!handles.has(id)) return;
    handles.delete(id);
    if (map.get(element) === release) map.delete(element);
    try {
      dispose();
    } catch {
      // A listener on a detached element is already gone.
    }
  };
  handles.set(id, release);
  map.set(element, release);
  return id;
}

function untrack(map, element) {
  if (!element) return;
  const release = map.get(element);
  if (release) release();
}

/** Undoes any registration made by a helper in this module, given the handle it returned. */
export function disposeHandle(handle) {
  const release = handles.get(handle);
  if (release) release();
}

/**
 * Watches a scroll container and reports whether the user is pinned to the bottom.
 * Auto-scroll follows new content only while pinned, so scrolling up to read is never interrupted.
 *
 * The container itself rarely changes size; what grows while a reply streams is the content inside
 * it. The resize observer therefore watches the first child, re-targeting if that child is replaced,
 * and a mutation observer catches growth that changes no element's box, such as text appended to a
 * paragraph that already spans the width.
 */
export function observeScroll(element, dotnet, threshold = 48) {
  if (!element) return 0;
  stopObservingScroll(element);

  let pinned = true;
  let reportFrame = 0;
  let followFrame = 0;
  let observed = null;

  const report = () => {
    reportFrame = 0;
    const distance = element.scrollHeight - element.scrollTop - element.clientHeight;
    const next = distance <= threshold;
    if (next !== pinned) {
      pinned = next;
      dotnet.invokeMethodAsync('OnPinnedChanged', pinned).catch(() => {});
    }
  };

  const onScroll = () => {
    if (reportFrame) return;
    reportFrame = requestAnimationFrame(report);
  };

  const follow = () => {
    if (!pinned || followFrame) return;
    followFrame = requestAnimationFrame(() => {
      followFrame = 0;
      if (pinned) element.scrollTop = element.scrollHeight;
    });
  };

  const resize = new ResizeObserver(follow);

  const retarget = () => {
    const content = element.firstElementChild;
    if (content === observed) return;
    if (observed) resize.unobserve(observed);
    observed = content;
    if (content) resize.observe(content);
  };

  const mutation = new MutationObserver(() => {
    retarget();
    follow();
  });

  element.addEventListener('scroll', onScroll, { passive: true });
  retarget();
  mutation.observe(element, { childList: true, subtree: true, characterData: true });
  report();

  return track(scrollObservers, element, () => {
    element.removeEventListener('scroll', onScroll);
    resize.disconnect();
    mutation.disconnect();
    if (reportFrame) cancelAnimationFrame(reportFrame);
    if (followFrame) cancelAnimationFrame(followFrame);
  });
}

export function stopObservingScroll(element) {
  untrack(scrollObservers, element);
}

/** The same as stopObservingScroll, under the name that pairs with observeScroll. */
export function unobserveScroll(element) {
  untrack(scrollObservers, element);
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

/** Clicks an element on .NET's behalf: how a real button opens a visually hidden file input. */
export function clickElement(element) {
  if (!element || typeof element.click !== 'function') return;
  element.click();
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
 * A short object URL for an image, so the composer can show a thumbnail without a data URL.
 *
 * A data URL for a thumbnail puts the whole image, base64 encoded, into the render tree and over
 * the circuit on every diff that touches it. An object URL is a few dozen characters and the bytes
 * never leave the browser. It is revoked by revokePreview when the attachment goes away.
 */
function createPreview(file) {
  if (!file || !file.type || !file.type.startsWith('image/')) return null;
  try {
    const url = URL.createObjectURL(file);
    previewUrls.add(url);
    return url;
  } catch {
    return null;
  }
}

/** A thumbnail URL for a file chosen through a file input, found by position and checked by name. */
export function previewFromInput(input, index, name) {
  const files = input?.files;
  if (!files || files.length === 0) return null;
  let file = files[index];
  if (!file || (name && file.name !== name)) file = Array.from(files).find((f) => f.name === name);
  return createPreview(file);
}

export function revokePreview(url) {
  if (!url || !previewUrls.has(url)) return;
  previewUrls.delete(url);
  try {
    URL.revokeObjectURL(url);
  } catch {
    // Already revoked.
  }
}

/**
 * Describes a file to .NET, with its content as a stream rather than inline.
 *
 * Sending the bytes in the interop message itself breaks on Blazor Server: the message would exceed
 * the SignalR limit (32 KB by default) for anything but a tiny file, and the file would be dropped
 * without a word. A stream reference is read in chunks, on Server and WebAssembly alike. A file over
 * the limit is still described, without content, so .NET can tell the user why it was refused; the
 * check happens here, before anything is read.
 */
function describeFile(file, fallbackName, maxSize) {
  const withinLimit = !(maxSize > 0) || file.size <= maxSize;
  return {
    name: file.name || fallbackName,
    contentType: file.type || 'application/octet-stream',
    size: file.size,
    content: withinLimit ? DotNet.createJSStreamReference(file) : null,
    previewUrl: withinLimit ? createPreview(file) : null,
  };
}

/**
 * Reports images pasted into the composer, so a screenshot on the clipboard becomes an attachment.
 * Text pastes are left alone for the browser to handle.
 */
export function registerPaste(element, dotnet, maxSize = 0) {
  if (!element) return 0;
  unregisterPaste(element);

  const handler = async (event) => {
    const items = Array.from(event.clipboardData?.items ?? []);
    const files = items.filter((i) => i.kind === 'file').map((i) => i.getAsFile()).filter(Boolean);
    if (files.length === 0) return;

    event.preventDefault();
    for (const file of files) {
      await dotnet.invokeMethodAsync('OnPastedFile', describeFile(file, 'pasted-image.png', maxSize))
        .catch(() => {});
    }
  };

  element.addEventListener('paste', handler);
  return track(pasteHandlers, element, () => element.removeEventListener('paste', handler));
}

export function unregisterPaste(element) {
  untrack(pasteHandlers, element);
}

const hasFiles = (event) => Boolean(event.dataTransfer?.types?.includes('Files'));

/** Reports files dropped onto an element, and toggles a class while a drag is over it. */
export function registerDrop(element, dotnet, maxSize = 0) {
  if (!element) return 0;
  unregisterDrop(element);

  let depth = 0;

  const setActive = (active) => {
    element.classList.toggle('bai-dropping', active);
    dotnet.invokeMethodAsync('OnDragStateChanged', active).catch(() => {});
  };

  const onEnter = (event) => {
    if (!hasFiles(event)) return;
    event.preventDefault();
    depth++;
    if (depth === 1) setActive(true);
  };

  const onOver = (event) => {
    if (hasFiles(event)) event.preventDefault();
  };

  // Only a drag that was counted on the way in is counted on the way out, or dragging text across
  // the composer would leave the depth negative and a later file drag unannounced.
  const onLeave = (event) => {
    if (!hasFiles(event) || depth === 0) return;
    depth--;
    if (depth === 0) setActive(false);
  };

  const onDrop = async (event) => {
    if (!event.dataTransfer?.files?.length) return;
    event.preventDefault();
    depth = 0;
    setActive(false);

    for (const file of Array.from(event.dataTransfer.files)) {
      await dotnet.invokeMethodAsync('OnDroppedFile', describeFile(file, 'file', maxSize))
        .catch(() => {});
    }
  };

  // A drag abandoned outside the window never fires dragleave on the element.
  const onEnd = () => {
    if (depth === 0) return;
    depth = 0;
    setActive(false);
  };

  element.addEventListener('dragenter', onEnter);
  element.addEventListener('dragover', onOver);
  element.addEventListener('dragleave', onLeave);
  element.addEventListener('drop', onDrop);
  window.addEventListener('dragend', onEnd);
  window.addEventListener('drop', onEnd);

  return track(dropHandlers, element, () => {
    element.removeEventListener('dragenter', onEnter);
    element.removeEventListener('dragover', onOver);
    element.removeEventListener('dragleave', onLeave);
    element.removeEventListener('drop', onDrop);
    window.removeEventListener('dragend', onEnd);
    window.removeEventListener('drop', onEnd);
    element.classList.remove('bai-dropping');
  });
}

export function unregisterDrop(element) {
  untrack(dropHandlers, element);
}

/** Whether this browser can transcribe speech without a server round trip. */
export function isDictationSupported() {
  return Boolean(window.SpeechRecognition || window.webkitSpeechRecognition);
}

/**
 * Starts dictation and returns a handle, or 0 when the browser cannot transcribe.
 *
 * One recogniser runs at a time: starting a second stops the first. Each gets its own handle so a
 * component disposing late cannot stop a recogniser another component has since started.
 */
export function startDictation(dotnet, language) {
  const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!Recognition) return 0;

  stopDictation();
  const current = new Recognition();
  const handle = nextHandle++;
  recognition = current;
  recognitionHandle = handle;
  current.lang = language || navigator.language || 'en-US';
  current.continuous = true;
  current.interimResults = true;

  current.onresult = (event) => {
    if (recognition !== current) return;
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
    recognitionHandle = 0;
    dotnet.invokeMethodAsync('OnDictationEnded').catch(() => {});
  };

  try {
    current.start();
  } catch {
    recognition = null;
    recognitionHandle = 0;
    return 0;
  }

  return handle;
}

/** Stops dictation. With a handle, stops only the recogniser that handle refers to. */
export function stopDictation(handle = 0) {
  if (!recognition) return;
  if (handle && handle !== recognitionHandle) return;
  const current = recognition;
  recognition = null;
  recognitionHandle = 0;
  try {
    current.stop();
  } catch {
    // Already stopped.
  }
}

/** The caret position in a text field, so the composer can open its menu where the user is typing. */
export function getCaret(element) {
  if (!element || typeof element.selectionStart !== 'number') return null;
  return element.selectionStart;
}

/** Both ends of the selection in a text field. */
export function getSelection(element) {
  if (!element || typeof element.selectionStart !== 'number') return null;
  return { start: element.selectionStart, end: element.selectionEnd };
}

/** Focuses a text field and puts the caret at a position, after .NET has rewritten its value. */
export function setCaret(element, position) {
  if (!element) return;
  element.focus({ preventScroll: true });
  if (typeof element.setSelectionRange !== 'function') return;
  const at = Math.max(0, Math.min(position, element.value.length));
  element.setSelectionRange(at, at);
}

export function prefersReducedMotion() {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/* Draft persistence: the composer text survives a page reload or a circuit that dropped. Session
   storage, not local storage, so a draft never follows the user to another tab or another day. */
const draftPrefix = 'bai-draft:';

export function readDraft(key) {
  if (!key) return null;
  try {
    return window.sessionStorage.getItem(draftPrefix + key);
  } catch {
    return null;
  }
}

export function writeDraft(key, text) {
  if (!key) return;
  try {
    if (text) window.sessionStorage.setItem(draftPrefix + key, text);
    else window.sessionStorage.removeItem(draftPrefix + key);
  } catch {
    // Storage full or blocked; the draft is a convenience, not a record.
  }
}

export function clearDraft(key) {
  writeDraft(key, '');
}

const menuKeys = ['ArrowDown', 'ArrowUp', 'Enter', 'Tab', 'Escape'];

/** Whether a key event belongs to an input method editor composing a character. */
const isComposing = (event) => event.isComposing || event.keyCode === 229;

/**
 * Stops Enter from inserting a newline when it is the submit key, and stops the keys that drive the
 * command and mention menu from also moving the caret or focus while that menu is open.
 *
 * Blazor cannot decide preventDefault per keystroke, so the decision is made here where the event
 * is. Enter alone submits when submitOnEnter is set; Ctrl or Cmd with Enter submits either way;
 * Shift or Alt with Enter is left to the browser, which inserts a newline. Enter during IME
 * composition is always the composer's, never a submit. The menu marks the textarea with
 * data-bai-menu-open while it is showing.
 */
export function registerSubmitKey(element, submitOnEnter) {
  if (!element) return 0;
  unregisterSubmitKey(element);

  const handler = (event) => {
    if (isComposing(event)) return;
    if (element.hasAttribute('data-bai-menu-open') && menuKeys.includes(event.key)) {
      event.preventDefault();
      return;
    }

    if (event.key !== 'Enter') return;
    if (event.shiftKey || event.altKey) return;
    const withModifier = event.ctrlKey || event.metaKey;
    if (!withModifier && !submitOnEnter) return;
    event.preventDefault();
  };

  element.addEventListener('keydown', handler);
  return track(submitKeyHandlers, element, () => element.removeEventListener('keydown', handler));
}

export function unregisterSubmitKey(element) {
  untrack(submitKeyHandlers, element);
}
