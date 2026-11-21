/**
 * ControlPlus icon for the card (20 November 2026): the register with a receipt, as vector paths in a 10 × 10 box, so it
 * prints sharp on the black-only thermal printer and in color on the PDF. Paths are SVG path data, also valid for Path2D.
 */
export const CARD_ICON_OUTLINE = [
  'M0.7 9.3 H7.4',                               // counter
  'M1.2 9.3 L2.5 5.6 H5.6 L6.2 9.3',              // register
  'M5.1 0.8 H8.9 V7.1 L8.1 7.7 L7.3 7.1 L6.5 7.7 L5.7 7.1 L5.1 7.5 Z', // receipt
  'M6.0 2.5 H8.0', 'M6.0 3.8 H8.0',               // receipt lines
];
export const CARD_ICON_CHECK = 'M6.2 5.4 L6.9 6.1 L8.1 4.7';

/** The icon as inline SVG of the given size in millimetres. */
export function cardIconSvg(sizeMm: number, color: string, checkColor = color): string {
  const strokes = CARD_ICON_OUTLINE.map(d => `<path d="${d}"/>`).join('');
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${sizeMm}mm" height="${sizeMm}mm" viewBox="0 0 10 10" fill="none" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><g stroke="${color}" stroke-width="0.7">${strokes}</g><path d="${CARD_ICON_CHECK}" stroke="${checkColor}" stroke-width="0.8"/></svg>`;
}
