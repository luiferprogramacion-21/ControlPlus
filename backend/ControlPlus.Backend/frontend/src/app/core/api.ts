import { HttpContext, HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { SessionService } from './session.service';

export interface Page<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number; }
export const authorizationAttempt = new HttpContextToken<boolean>(() => false);
export const authorizationContext = (): HttpContext => new HttpContext().set(authorizationAttempt, true);

export function errorText(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'No se pudo completar la operación.';
  const body = error.error;
  const safeProblemResponse = [400, 403, 404, 409, 422].includes(error.status);
  if (safeProblemResponse && body && typeof body === 'object') {
    const validation = body.errors as Record<string, string[]> | undefined;
    if (validation) {
      const messages = Object.values(validation).flat().filter(message => typeof message === 'string');
      if (messages.length) return messages.join(' ');
    }
    if (typeof body.title === 'string' && body.title.trim()) return body.title;
  }
  if (error.status === 0) return 'No se puede conectar con la API local. Comprueba que está ejecutándose.';
  if (error.status === 401) return 'La sesión expiró. Ingresa de nuevo.';
  if (error.status === 403) return 'No tienes permiso para esta operación.';
  if (error.status === 409) return 'Los datos cambiaron o ya existen. Actualiza la lista e inténtalo de nuevo.';
  return 'Ocurrió un error al procesar la solicitud.';
}

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(SessionService);
  const router = inject(Router);
  const token = session.token();
  const authenticated = token && !request.url.endsWith('/api/auth/login');
  return next(authenticated ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request).pipe(
    catchError(error => {
      const rejectedAuthorizer = request.context.get(authorizationAttempt) &&
        ['authentication.invalid_credentials', 'authentication.unauthorized'].includes(error?.error?.code);
      if (error instanceof HttpErrorResponse && error.status === 401 && authenticated && !rejectedAuthorizer) {
        session.logout();
        void router.navigateByUrl('/ingresar');
      }
      return throwError(() => error);
    }),
  );
};
