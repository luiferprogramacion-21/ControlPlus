import { Injectable } from '@angular/core';
import { code128Widths } from '../../core/code128';
import { CredentialCard } from './credential-card';
import { CARD_ICON_CHECK, CARD_ICON_OUTLINE } from './credential-card-icon';

const NAVY = '#0F2C4D';
const ORANGE = '#FD6D32';

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
  // Color design (20 November 2026, at the user's request): navy band with the icon and the business name in white, an
  // orange line, the name in navy, the role in an orange label, and the barcode on white (the reader needs white around it).
  ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = NAVY; ctx.fillRect(0, 0, canvas.width, Math.round(15 * s));
  ctx.fillStyle = ORANGE; ctx.fillRect(0, Math.round(15 * s), canvas.width, Math.round(1.2 * s));
  drawIcon(ctx, 4.5 * s, 2.5 * s, 10 * s);
  ctx.fillStyle = '#fff'; ctx.textBaseline = 'alphabetic'; ctx.textAlign = 'left';
  ctx.font = `bold ${5 * s}px Arial, Helvetica, sans-serif`; ctx.fillText(card.businessName, 17.5 * s, 8.6 * s, (CARD_WIDTH_MM - 22) * s);
  ctx.font = `${2.4 * s}px Arial, Helvetica, sans-serif`; ctx.fillText('CARNÉ DE OPERADOR', 17.5 * s, 12.6 * s);
  ctx.fillStyle = NAVY; ctx.font = `bold ${4.4 * s}px Arial, Helvetica, sans-serif`; ctx.fillText(card.displayName, 5 * s, 22 * s, (CARD_WIDTH_MM - 10) * s);
  const role = card.roleName.toUpperCase();
  ctx.font = `bold ${2.4 * s}px Arial, Helvetica, sans-serif`;
  const roleWidth = ctx.measureText(role).width + 3 * s;
  ctx.fillStyle = ORANGE; roundRect(ctx, 5 * s, 23.6 * s, roleWidth, 3.6 * s, 0.9 * s); ctx.fill();
  ctx.fillStyle = '#fff'; ctx.fillText(role, 6.5 * s, 26.3 * s);
  ctx.fillStyle = '#4A5568'; ctx.font = `${2.6 * s}px Arial, Helvetica, sans-serif`; ctx.fillText(card.userName, 5 * s + roleWidth + 2 * s, 26.3 * s, (CARD_WIDTH_MM - 15) * s - roleWidth);
  // Barcode (20 November 2026): the scanner read the printed PDF only sometimes. The quiet zone was 2.5 mm, half of the
  // 10 modules Code 128 needs, and fractional bar widths came out with gray edges. Now each bar is a whole number of pixels
  // (6 px = 0.5 mm, like the thermal card), with 10 blank modules each side, centred; narrower only if a code does not fit.
  const widths = code128Widths(card.token);
  const total = widths.reduce((sum, width) => sum + width, 0) + 20;
  const modulePx = Math.max(1, Math.min(Math.round(0.5 * s), Math.floor((CARD_WIDTH_MM - 6) * s / total)));
  let x = Math.round((canvas.width - total * modulePx) / 2) + 10 * modulePx;
  ctx.fillStyle = '#000';
  widths.forEach((width, index) => { if (index % 2 === 0) ctx.fillRect(x, Math.round(29.5 * s), width * modulePx, Math.round(15 * s)); x += width * modulePx; });
  ctx.fillStyle = NAVY; ctx.textAlign = 'center'; ctx.font = `${2.4 * s}px Arial, Helvetica, sans-serif`; ctx.fillText(`…${card.fragment}`, (CARD_WIDTH_MM / 2) * s, 48.8 * s);
  // Thin frame so the card is easy to cut out.
  ctx.strokeStyle = '#B7C2D0'; ctx.lineWidth = 0.3 * s; ctx.strokeRect(0.15 * s, 0.15 * s, canvas.width - 0.3 * s, canvas.height - 0.3 * s);
  return true;
}

/** The ControlPlus icon in white with the orange check, drawn from the shared vector paths. */
function drawIcon(ctx: CanvasRenderingContext2D, x: number, y: number, size: number): void {
  const k = size / 10;
  ctx.save(); ctx.translate(x, y); ctx.scale(k, k); ctx.lineCap = 'round'; ctx.lineJoin = 'round';
  ctx.strokeStyle = '#fff'; ctx.lineWidth = 0.7;
  for (const d of CARD_ICON_OUTLINE) ctx.stroke(new Path2D(d));
  ctx.strokeStyle = ORANGE; ctx.lineWidth = 0.8; ctx.stroke(new Path2D(CARD_ICON_CHECK));
  ctx.restore();
}

function roundRect(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number): void {
  ctx.beginPath(); ctx.moveTo(x + r, y); ctx.arcTo(x + w, y, x + w, y + h, r); ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r); ctx.arcTo(x, y, x + w, y, r); ctx.closePath();
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
    return new Promise((resolve, reject) => canvas.toBlob(blob => blob ? resolve(blob) : reject(new Error('No se pudo generar la imagen.')), 'image/jpeg', 1));
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
