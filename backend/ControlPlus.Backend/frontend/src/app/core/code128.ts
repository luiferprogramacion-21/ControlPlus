/**
 * Code 128 (subset B, printable ASCII) encoder. Each entry lists the widths, in modules, of the alternating bars and spaces
 * of one symbol; the stop symbol has seven elements. Value 104 is "Start B".
 */
const PATTERNS = [
  '212222', '222122', '222221', '121223', '121322', '131222', '122213', '122312', '132212', '221213',
  '221312', '231212', '112232', '122132', '122231', '113222', '123122', '123221', '223211', '221132',
  '221231', '213212', '223112', '312131', '311222', '321122', '321221', '312212', '322112', '322211',
  '212123', '212321', '232121', '111323', '131123', '131321', '112313', '132113', '132311', '211313',
  '231113', '231311', '112133', '112331', '132131', '113123', '113321', '133121', '313121', '211331',
  '231131', '213113', '213311', '213131', '311123', '311321', '331121', '312113', '312311', '332111',
  '314111', '221411', '431111', '111224', '111422', '121124', '121421', '141122', '141221', '112214',
  '112412', '122114', '122411', '142112', '142211', '241211', '221114', '413111', '241112', '134111',
  '111242', '121142', '121241', '114212', '124112', '124211', '411212', '421112', '421211', '212141',
  '214121', '412121', '111143', '111341', '131141', '114113', '114311', '411113', '411311', '113141',
  '114131', '311141', '411131', '211412', '211214', '211232', '2331112',
] as const;
const START_B = 104;
const START_C = 105;
const STOP = 106;

/** Symbol values (start, data, checksum, stop). Even-length digit strings use subset C (two digits per symbol); other text uses subset B. */
export function code128Values(text: string): number[] {
  if (!text) throw new Error('El código está vacío.');
  if (/^(\d\d)+$/.test(text)) {
    const pairs = [START_C, ...(text.match(/\d\d/g) ?? []).map(Number)];
    const sum = pairs.reduce((total, value, index) => total + value * (index === 0 ? 1 : index), 0) % 103;
    return [...pairs, sum, STOP];
  }
  const values = [START_B];
  for (const char of text) {
    const code = char.charCodeAt(0);
    if (code < 32 || code > 126) throw new Error('El código tiene un carácter que no se puede poner en el código de barras.');
    values.push(code - 32);
  }
  const checksum = values.reduce((sum, value, index) => sum + value * (index === 0 ? 1 : index), 0) % 103;
  return [...values, checksum, STOP];
}

/** Widths of every bar and space, in modules, starting with a bar. */
export function code128Widths(text: string): number[] {
  return code128Values(text).flatMap(value => [...PATTERNS[value]].map(Number));
}

/** Standalone SVG of the barcode. It carries no fixed size (it fills its container) so it can never overflow it. */
export function code128Svg(text: string, moduleWidth = 2, height = 60): string {
  const widths = code128Widths(text);
  const quiet = 10;
  let x = quiet; let bars = '';
  widths.forEach((width, index) => {
    if (index % 2 === 0) bars += `<rect x="${x * moduleWidth}" y="0" width="${width * moduleWidth}" height="${height}"/>`;
    x += width;
  });
  const total = (x + quiet) * moduleWidth;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${total} ${height}" style="display:block;width:100%;height:auto" role="img" aria-label="Código de barras Code 128" shape-rendering="crispEdges" fill="#000">${bars}</svg>`;
}
