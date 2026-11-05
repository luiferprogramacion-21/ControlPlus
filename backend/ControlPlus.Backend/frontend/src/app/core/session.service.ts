import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface User { id: string; userName: string; displayName: string; roles: string[]; permissions: string[]; }
interface LoginResponse { accessToken: string; expiresAtUtc: string; user: User; }
const KEY = 'controlplus.session';

@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  readonly token = signal<string | null>(null);
  readonly user = signal<User | null>(null);
  private expiresAt = 0;

  constructor() {
    try {
      const saved = JSON.parse(sessionStorage.getItem(KEY) ?? 'null') as LoginResponse | null;
      if (saved?.accessToken && Date.parse(saved.expiresAtUtc) > Date.now()) this.set(saved);
      else sessionStorage.removeItem(KEY);
    } catch { sessionStorage.removeItem(KEY); }
  }

  async login(userName: string, password: string): Promise<void> {
    const result = await firstValueFrom(this.http.post<LoginResponse>('/api/auth/login', { userName, password }));
    this.set(result);
    sessionStorage.setItem(KEY, JSON.stringify(result));
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

  has(permission: string): boolean { return this.user()?.permissions.some(code => code.trim().toUpperCase() === permission.toUpperCase()) ?? false; }
  hasRole(...codes: string[]): boolean {
    const accepted = new Set(codes.map(code => code.toUpperCase()));
    return this.user()?.roles.some(code => accepted.has(code.trim().toUpperCase())) ?? false;
  }
  logout(): void {
    this.token.set(null);
    this.user.set(null);
    this.expiresAt = 0;
    sessionStorage.removeItem(KEY);
  }
  private set(result: LoginResponse): void {
    this.token.set(result.accessToken);
    this.user.set(result.user);
    this.expiresAt = Date.parse(result.expiresAtUtc);
  }
}
