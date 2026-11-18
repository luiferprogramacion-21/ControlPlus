import { Injectable } from '@angular/core';
import { code128Widths } from '../../core/code128';
import { CredentialCard } from './credential-card';

export const CARD_WIDTH_MM = 85.6;
export const CARD_HEIGHT_MM = 54;
/** About 305 dpi: sharp enough for a printed card and for a scanner. */
const PX_PER_MM = 12;

/** Draws the card on a canvas exactly as it is printed (same layout in millimetres). */
export function drawCredentialCard(canvas: HTMLCanvasElement, card: CredentialCard): boolean {
  canvas.width = Math.round(CARD_WIDTH_MM * PX_PER_MM); canvas.height = Math.round(CARD_HEIGHT_MM * PX_PER_MM);
  const ctx = canvas.getContext('2d');
  if (!ctx) return false;
  const s = PX_PER_MM;
  ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.strokeStyle = '#000'; ctx.lineWidth = 0.3 * s; ctx.strokeRect(0.15 * s, 0.15 * s, canvas.width - 0.3 * s, canvas.height - 0.3 * s);
  ctx.fillStyle = '#000'; ctx.textBaseline = 'alphabetic';
  ctx.font = `bold ${4.6 * s}px Arial, Helvetica, sans-serif`; ctx.textAlign = 'left'; ctx.fillText(card.businessName, 5 * s, 10 * s, 48 * s);
  ctx.font = `${2.5 * s}px Arial, Helvetica, sans-serif`; ctx.textAlign = 'right'; ctx.fillText('CREDENCIAL DE OPERADOR', (CARD_WIDTH_MM - 5) * s, 10 * s);
  ctx.textAlign = 'left';
  ctx.font = `bold ${3.9 * s}px Arial, Helvetica, sans-serif`; ctx.fillText(card.displayName, 5 * s, 19 * s, (CARD_WIDTH_MM - 10) * s);
  ctx.font = `${2.8 * s}px Arial, Helvetica, sans-serif`; ctx.fillText(`${card.roleName} · ${card.userName}`, 5 * s, 23.5 * s, (CARD_WIDTH_MM - 10) * s);
  // Barcode: the widest module size that fits between the margins (with a quiet zone), bars 15 mm tall.
  const widths = code128Widths(card.token);
  const total = widths.reduce((sum, width) => sum + width, 0);
  const quiet = 2.5; const available = CARD_WIDTH_MM - 10 - 2 * quiet;
  const module = available / total;
  let x = 5 + quiet;
  widths.forEach((width, index) => { if (index % 2 === 0) ctx.fillRect(x * s, 27.5 * s, width * module * s, 15 * s); x += width * module; });
  ctx.textAlign = 'center'; ctx.font = `${2.4 * s}px Arial, Helvetica, sans-serif`; ctx.fillText(`…${card.fragment}`, (CARD_WIDTH_MM / 2) * s, 48.5 * s);
  return true;
}

/** A one-page PDF (page = the card) that holds the JPEG image. */
export function buildPdfFromJpeg(jpeg: Uint8Array, widthPx: number, heightPx: number, widthMm = CARD_WIDTH_MM, heightMm = CARD_HEIGHT_MM): Uint8Array {
  const encoder = new TextEncoder();
  const pt = (mm: number) => (mm * 72 / 25.4).toFixed(2);
  const chunks: Uint8Array[] = []; const offsets: number[] = []; let length = 0;
  const push = (part: Uint8Array) => { chunks.push(part); length += part.length; };
  const text = (value: string) => push(encoder.encode(value));
  const object = (id: number, body: () => void) => { offsets[id] = length; text(`${id} 0 obj\n`); body(); text('endobj\n'); };
  const content = `q ${pt(widthMm)} 0 0 ${pt(heightMm)} 0 0 cm /Im0 Do Q`;
  text('%PDF-1.4\n');
  object(1, () => text('<< /Type /Catalog /Pages 2 0 R >>\n'));
  object(2, () => text('<< /Type /Pages /Kids [3 0 R] /Count 1 >>\n'));
  object(3, () => text(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${pt(widthMm)} ${pt(heightMm)}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>\n`));
  object(4, () => {
    text(`<< /Type /XObject /Subtype /Image /Width ${widthPx} /Height ${heightPx} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length ${jpeg.length} >>\nstream\n`);
    push(jpeg); text('\nendstream\n');
  });
  object(5, () => text(`<< /Length ${content.length} >>\nstream\n${content}\nendstream\n`));
  const xref = length;
  text('xref\n0 6\n0000000000 65535 f \n');
  for (let id = 1; id <= 5; id++) text(`${String(offsets[id]).padStart(10, '0')} 00000 n \n`);
  text(`trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`);
  const result = new Uint8Array(length); let position = 0;
  for (const chunk of chunks) { result.set(chunk, position); position += chunk.length; }
  return result;
}

/** Injectable so the screen and its tests never depend on the canvas or on a real download. */
@Injectable({ providedIn: 'root' })
export class CredentialCardExporter {
  async jpeg(card: CredentialCard): Promise<Blob> {
    const canvas = document.createElement('canvas');
    if (!drawCredentialCard(canvas, card)) throw new Error('El navegador no puede dibujar la imagen.');
    return new Promise((resolve, reject) => canvas.toBlob(blob => blob ? resolve(blob) : reject(new Error('No se pudo generar la imagen.')), 'image/jpeg', 0.95));
  }
  async pdf(card: CredentialCard): Promise<Blob> {
    const image = await this.jpeg(card);
    const bytes = new Uint8Array(await image.arrayBuffer());
    return new Blob([buildPdfFromJpeg(bytes, Math.round(CARD_WIDTH_MM * PX_PER_MM), Math.round(CARD_HEIGHT_MM * PX_PER_MM)) as BlobPart], { type: 'application/pdf' });
  }
  save(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url; link.download = fileName; link.style.display = 'none';
    document.body.appendChild(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}
