import { el, button, notify } from './shared.js';

// Opens one file from a channel: images, text and code, PDFs, audio and video in place; anything else can be
// downloaded. It is opened from files, not from the start menu.
const TEXT_TYPES = ['application/json', 'application/xml', 'application/javascript', 'application/x-javascript', 'application/yaml', 'application/x-yaml', 'application/toml', 'application/x-sh', 'application/sql'];
const TEXT_LIMIT = 2000000;

export function isText(mediaType) {
  return mediaType.startsWith('text/') || TEXT_TYPES.includes(mediaType) || mediaType.endsWith('+json') || mediaType.endsWith('+xml');
}

export function downloadFile(url, name) {
  const link = el('a'); link.href = url; link.download = name; document.body.append(link); link.click(); link.remove();
}

// Copies the file itself where the clipboard can hold it (text, images as PNG), else a link to it. Returns what was copied.
export async function copyFile(url, mediaType) {
  if (!navigator.clipboard || !window.isSecureContext) { throw new Error('Copying files needs a secure address, such as localhost.'); }
  if (isText(mediaType)) { await navigator.clipboard.writeText(await (await fetch(url)).text()); return 'Text copied.'; }
  if (mediaType.startsWith('image/') && window.ClipboardItem) {
    await navigator.clipboard.write([new ClipboardItem({ 'image/png': imageAsPng(url) })]);
    return 'Image copied.';
  }
  await navigator.clipboard.writeText(new URL(url, location.href).href);
  return 'Link copied.';
}

async function imageAsPng(url) {
  const blob = await (await fetch(url)).blob();
  if (blob.type === 'image/png') { return blob; }
  const bitmap = await createImageBitmap(blob);
  const canvas = el('canvas'); canvas.width = bitmap.width; canvas.height = bitmap.height;
  canvas.getContext('2d').drawImage(bitmap, 0, 0);
  return new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
}

export async function copyWithNotice(url, mediaType) {
  try { notify(await copyFile(url, mediaType)); }
  catch (error) { notify(`Could not copy. ${error.message}`, true); }
}

export const viewerApplet = { id: 'viewer', title: 'File Viewer', icon: '▣', description: 'Shows a file from a channel.', hidden: true, size: { width: 720, height: 576 }, mount };

async function mount(ctx) {
  const { url, name, mediaType } = ctx.state;
  if (!url) { ctx.body.replaceChildren(el('div', 'empty', 'No file to show.')); return; }
  ctx.setTitle(name);
  const bar = el('div', 'toolbar viewer-toolbar');
  bar.append(el('strong', 'viewer-name', name), el('small', 'muted viewer-type', mediaType), button('Download', () => downloadFile(url, name), 'small'), button('Copy', () => copyWithNotice(url, mediaType), 'small'));
  const stage = el('div', 'viewer-stage');
  ctx.body.replaceChildren(bar, stage);
  stage.append(await content(url, name, mediaType));
}

async function content(url, name, mediaType) {
  if (mediaType.startsWith('image/')) { const image = el('img', 'viewer-image'); image.src = url; image.alt = name; return image; }
  if (mediaType.startsWith('video/')) { const video = el('video', 'viewer-media'); video.src = url; video.controls = true; return video; }
  if (mediaType.startsWith('audio/')) { const audio = el('audio', 'viewer-audio'); audio.src = url; audio.controls = true; return audio; }
  if (mediaType === 'application/pdf') { const frame = el('iframe', 'viewer-frame'); frame.src = url; frame.title = name; return frame; }
  if (isText(mediaType)) {
    const response = await fetch(url);
    if (!response.ok) { return el('div', 'empty', `The file could not be read (${response.status}).`); }
    const text = await response.text();
    const shown = text.length > TEXT_LIMIT ? `${text.slice(0, TEXT_LIMIT)}\n\n…` : text;
    return el('pre', 'viewer-text', shown);
  }
  return el('div', 'empty', 'This kind of file cannot be shown here. Download it to open it.');
}
