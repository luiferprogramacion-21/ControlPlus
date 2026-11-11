import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Page } from '../../core/api';

/** RoleLevel is serialized by the API as its numeric value. */
export const ROLE_LEVEL_NAMES: Record<number, string> = { 0: 'Sin nivel', 1: 'Cajero', 2: 'Supervisor', 3: 'Administrador' };

export interface RoleSummary { id: string; code: string; name: string; level: number; isActive: boolean; }
export interface AdminUser {
  id: string; userName: string; displayName: string; isActive: boolean; isLocked: boolean;
  failedLoginAttempts: number; lockedAtUtc: string | null; lastLoginAtUtc: string | null; roles: RoleSummary[];
}
export interface IssuedCredential { credentialId: string; userId: string; format: string; credentialToken: string; visibleFragment: string; issuedAtUtc: string; expiresAtUtc: string | null; }
export interface AdminPermission { id: string; code: string; name: string; description: string | null; module: string; isActive: boolean; }
export interface AdminRole extends RoleSummary { permissions: AdminPermission[]; }
/** Source is ROL or INDIVIDUAL; individualEffect is CONCEDER or REVOCAR when an exception exists. */
export interface EffectivePermission { id: string; code: string; name: string; granted: boolean; source: string; individualEffect: string | null; }
export interface CreateUserInput { userName: string; displayName: string; password: string; roleIds: string[]; }

@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);

  users(query: { search?: string; isActive?: boolean | null; isLocked?: boolean | null; page: number; pageSize: number }): Promise<Page<AdminUser>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.search) params = params.set('search', query.search);
    if (query.isActive != null) params = params.set('isActive', query.isActive);
    if (query.isLocked != null) params = params.set('isLocked', query.isLocked);
    return firstValueFrom(this.http.get<Page<AdminUser>>('/api/users', { params }));
  }
  /** Issues the Code 128 operator credential of a user; the token is returned only this once and the previous one is revoked. */
  issueCredential(userId: string): Promise<IssuedCredential> { return firstValueFrom(this.http.post<IssuedCredential>(`/api/cash/operator-credentials/${userId}`, { expiresAtUtc: null })); }
  createUser(input: CreateUserInput): Promise<AdminUser> { return firstValueFrom(this.http.post<AdminUser>('/api/users', input)); }
  updateUser(id: string, displayName: string): Promise<AdminUser> { return firstValueFrom(this.http.put<AdminUser>(`/api/users/${id}`, { displayName })); }
  setUserActive(user: AdminUser): Promise<AdminUser> {
    return firstValueFrom(this.http.put<AdminUser>(`/api/users/${user.id}/${user.isActive ? 'deactivate' : 'activate'}`, {}));
  }
  reactivateUser(id: string): Promise<AdminUser> { return firstValueFrom(this.http.post<AdminUser>(`/api/users/${id}/reactivate`, {})); }
  assignRole(userId: string, roleId: string): Promise<AdminUser> { return firstValueFrom(this.http.post<AdminUser>(`/api/users/${userId}/roles`, { roleId })); }
  /** The server requires the current password only when the actor changes their own password. */
  changePassword(userId: string, newPassword: string, currentPassword = ''): Promise<void> {
    return firstValueFrom(this.http.put<void>(`/api/users/${userId}/password`, { currentPassword, newPassword }));
  }
  effectivePermissions(userId: string): Promise<EffectivePermission[]> {
    return firstValueFrom(this.http.get<EffectivePermission[]>(`/api/users/${userId}/permissions/effective`));
  }
  setPermissionOverride(userId: string, permissionId: string, granted: boolean): Promise<void> {
    return firstValueFrom(this.http.put<void>(`/api/users/${userId}/permissions/${permissionId}`, { granted }));
  }
  resetPermissionOverrides(userId: string): Promise<void> { return firstValueFrom(this.http.delete<void>(`/api/users/${userId}/permissions`)); }
  grantRolePermission(roleId: string, permissionId: string): Promise<AdminRole> {
    return firstValueFrom(this.http.post<AdminRole>(`/api/roles/${roleId}/permissions/${permissionId}`, {}));
  }
  revokeRolePermission(roleId: string, permissionId: string): Promise<AdminRole> {
    return firstValueFrom(this.http.delete<AdminRole>(`/api/roles/${roleId}/permissions/${permissionId}`));
  }
  resetRolePermissions(roleId: string): Promise<AdminRole> { return firstValueFrom(this.http.post<AdminRole>(`/api/roles/${roleId}/permissions/reset`, {})); }
  /** Reads the whole catalogue, 200 at a time (the server maximum page size). */
  async permissionCatalog(): Promise<AdminPermission[]> {
    const all: AdminPermission[] = [];
    let page = 1; let totalPages = 1;
    do {
      const params = new HttpParams().set('page', page).set('pageSize', 200).set('isActive', true);
      const result = await firstValueFrom(this.http.get<Page<AdminPermission>>('/api/permissions', { params }));
      all.push(...result.items); totalPages = result.totalPages; page++;
    } while (page <= totalPages);
    return all;
  }
  roles(query: { isActive?: boolean | null; page: number; pageSize: number }): Promise<Page<AdminRole>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.isActive != null) params = params.set('isActive', query.isActive);
    return firstValueFrom(this.http.get<Page<AdminRole>>('/api/roles', { params }));
  }
}
