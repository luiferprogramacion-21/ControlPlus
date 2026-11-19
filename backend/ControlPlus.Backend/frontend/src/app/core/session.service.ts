import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface User { id: string; userName: string; displayName: string; roles: string[]; permissions: string[]; }
interface LoginResponse { accessToken: string; expiresAtUtc: string; user: User; passwordChangeRequired?: boolean; }
/** Pantalla para cambiar la contraseña cuando la sesión lo exige (contraseñas de 4 números, 19 de noviembre de 2026). */
export const PASSWORD_CHANGE_ROUTE = '/cambiar-contrasena';
const KEY = 'controlplus.session';
/** Cada cuánto se revisa si conviene renovar la sesión. */
export const KEEP_ALIVE_INTERVAL_MS = 30_000;

/**
 * Sesión del usuario. Mientras se usa el sistema (teclado, mouse o consultas a la API) el token se renueva solo al
 * pasar la mitad de su duración, para que una venta en curso no se pierda al vencer. Si nadie la usa durante toda la
 * duración del token (30 minutos por defecto), vence como siempre y la siguiente consulta lleva al inicio de sesión.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  readonly token = signal<string | null>(null);
  readonly user = signal<User | null>(null);
  /** La contraseña con la que entró ya no cumple la regla (4 números): solo puede cambiarla. */
  readonly passwordChangeRequired = signal(false);
  private expiresAt = 0;
  private receivedAt = 0;
  private lastActivity = 0;
  private renewing: Promise<void> | null = null;
  private keepAliveTimer: ReturnType<typeof setInterval> | undefined;

  constructor() {
    try {
      const saved = JSON.parse(sessionStorage.getItem(KEY) ?? 'null') as LoginResponse | null;
      if (saved?.accessToken && Date.parse(saved.expiresAtUtc) > Date.now()) this.set(saved);
      else sessionStorage.removeItem(KEY);
    } catch { sessionStorage.removeItem(KEY); }
    if (typeof document !== 'undefined') {
      for (const type of ['pointerdown', 'keydown']) document.addEventListener(type, () => this.touch(), { capture: true, passive: true });
    }
  }

  async login(userName: string, password: string): Promise<void> {
    const result = await firstValueFrom(this.http.post<LoginResponse>('/api/auth/login', { userName, password }));
    this.store(result);
  }

  /** Cambia la contraseña cuando la sesión lo exige; el servidor devuelve una sesión nueva sin la restricción. */
  async changeRequiredPassword(currentPassword: string, newPassword: string): Promise<void> {
    const result = await firstValueFrom(this.http.post<LoginResponse>('/api/auth/password-change', { currentPassword, newPassword }));
    this.store(result);
  }

  async validate(): Promise<boolean> {
    if (!this.token() || Date.now() >= this.expiresAt) { this.logout(); return false; }
    try {
      await this.refreshUser();
      return true;
    } catch { this.logout(); return false; }
  }

  async refreshUser(): Promise<void> {
    this.user.set(await firstValueFrom(this.http.get<User>('/api/auth/me')));
  }

  /** Marca que el usuario está trabajando (tecla, clic o consulta a la API). */
  touch(now = Date.now()): void { this.lastActivity = now; }

  /**
   * Renueva la sesión si hubo actividad desde que se recibió el token actual y ya pasó la mitad de su duración.
   * Con la sesión vencida no hace nada: la siguiente consulta recibe 401 y lleva al inicio de sesión.
   */
  async keepAlive(now = Date.now()): Promise<void> {
    if (!this.token() || this.renewing || now >= this.expiresAt) return;
    const lifetime = this.expiresAt - this.receivedAt;
    if (this.expiresAt - now > lifetime / 2 || this.lastActivity <= this.receivedAt) return;
    await this.renew();
  }

  /** Pide un token nuevo para la sesión vigente. Un 401 lo maneja el interceptor (cierra la sesión). */
  renew(): Promise<void> {
    const current = this.token();
    if (!current) return Promise.resolve();
    this.renewing ??= firstValueFrom(this.http.post<LoginResponse>('/api/auth/refresh', null))
      .then(result => { if (this.token() === current) this.store(result); })
      .catch(() => undefined)
      .finally(() => { this.renewing = null; });
    return this.renewing;
  }

  has(permission: string): boolean { return this.user()?.permissions.some(code => code.trim().toUpperCase() === permission.toUpperCase()) ?? false; }
  hasRole(...codes: string[]): boolean {
    const accepted = new Set(codes.map(code => code.toUpperCase()));
    return this.user()?.roles.some(code => accepted.has(code.trim().toUpperCase())) ?? false;
  }
  logout(): void {
    this.token.set(null);
    this.user.set(null);
    this.passwordChangeRequired.set(false);
    this.expiresAt = 0;
    this.receivedAt = 0;
    if (this.keepAliveTimer !== undefined) { clearInterval(this.keepAliveTimer); this.keepAliveTimer = undefined; }
    sessionStorage.removeItem(KEY);
  }
  private store(result: LoginResponse): void {
    this.set(result);
    sessionStorage.setItem(KEY, JSON.stringify(result));
  }
  private set(result: LoginResponse): void {
    this.token.set(result.accessToken);
    this.user.set(result.user);
    this.passwordChangeRequired.set(!!result.passwordChangeRequired);
    this.expiresAt = Date.parse(result.expiresAtUtc);
    this.receivedAt = Date.now();
    this.keepAliveTimer ??= setInterval(() => void this.keepAlive(), KEEP_ALIVE_INTERVAL_MS);
  }
}
