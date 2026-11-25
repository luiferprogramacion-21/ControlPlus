/** Validación ligera de correo para la pantalla; el servidor conserva la validación definitiva. */
export function isEmailAddress(value: string): boolean {
  if (!value || value.length > 254 || [...value].some(character => character.trim() === '')) return false;
  const at = value.indexOf('@');
  if (at <= 0 || at !== value.lastIndexOf('@') || at === value.length - 1) return false;
  const dot = value.indexOf('.', at + 2);
  return dot > at + 1 && dot < value.length - 1;
}
