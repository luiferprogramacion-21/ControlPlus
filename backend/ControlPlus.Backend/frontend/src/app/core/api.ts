import { HttpContext, HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { PASSWORD_CHANGE_ROUTE, SessionService } from './session.service';

export interface Page<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number; }
export const authorizationAttempt = new HttpContextToken<boolean>(() => false);
export const authorizationContext = (): HttpContext => new HttpContext().set(authorizationAttempt, true);

/** Server messages written for programmers (20 November 2026): never shown as they are to a shop employee. */
const TECHNICAL_MESSAGE = /Idempotency|UUID|GUID|JSON|Code 128|\b[a-z]+Id\b|authorization|endpoint|ExpectedVersion|expectedVersion/;
export const TECHNICAL_FALLBACK = 'No se pudo completar la operación. Actualiza la pantalla e inténtalo de nuevo; si se repite, avisa al Administrador.';
const plain = (message: string): string => TECHNICAL_MESSAGE.test(message) ? TECHNICAL_FALLBACK : message;

export function errorText(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'No se pudo completar la operación.';
  const body = error.error;
  const safeProblemResponse = [400, 403, 404, 409, 422].includes(error.status);
  if (safeProblemResponse && body && typeof body === 'object') {
    const validation = body.errors as Record<string, string[]> | undefined;
    if (validation) {
      const messages = Object.values(validation).flat().filter(message => typeof message === 'string');
      if (messages.length) return plain(messages.join(' '));
    }
    if (typeof body.title === 'string' && body.title.trim()) return plain(body.title);
  }
  if (error.status === 0) return 'No se puede conectar con ControlPlus. Espera un momento y vuelve a intentarlo; si sigue igual, reinicia el computador.';
  if (error.status === 401) return 'La sesión expiró. Ingresa de nuevo.';
  if (error.status === 403) return 'No tienes permiso para esta operación.';
  if (error.status === 409) return 'Los datos cambiaron o ya existen. Actualiza la lista e inténtalo de nuevo.';
  return 'Ocurrió un error al procesar la solicitud.';
}

/** Texto genérico de «prohibido» del servidor: no explica nada, así que la pantalla usa el suyo. */
const GENERIC_FORBIDDEN = 'No tiene permiso para realizar esta acción.';

/**
 * Auditoría del 23 de noviembre de 2026: cuando el servidor explica por qué rechazó algo (por ejemplo, «La caja está abierta
 * en turno individual por otra persona…»), se muestra ese motivo. El texto propio de la pantalla queda solo para un rechazo
 * sin explicación.
 */
export function reasonOr(error: unknown, fallback: string): string {
  if (error instanceof HttpErrorResponse && error.error && typeof error.error === 'object') {
    const title = typeof error.error.title === 'string' ? error.error.title.trim() : '';
    if (title && title !== GENERIC_FORBIDDEN && !TECHNICAL_MESSAGE.test(title)) return title;
  }
  return fallback;
}

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(SessionService);
  const router = inject(Router);
  const token = session.token();
  const authenticated = token && !request.url.endsWith('/api/auth/login');
  // Consultar la API cuenta como actividad para renovar la sesión (la propia renovación no).
  if (authenticated && !request.url.endsWith('/api/auth/refresh')) session.touch();
  return next(authenticated ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request).pipe(
    catchError(error => {
      const rejectedAuthorizer = request.context.get(authorizationAttempt) &&
        ['authentication.invalid_credentials', 'authentication.unauthorized'].includes(error?.error?.code);
      if (error instanceof HttpErrorResponse && error.status === 401 && authenticated && !rejectedAuthorizer) {
        session.logout();
        void router.navigateByUrl('/ingresar');
      }
      if (error instanceof HttpErrorResponse && error.status === 403 && error.error?.code === 'password.change_required') {
        session.passwordChangeRequired.set(true);
        void router.navigateByUrl(PASSWORD_CHANGE_ROUTE);
      }
      return throwError(() => error);
    }),
  );
};

/** Request body of a second-user authorization: user name and password, or the scanned Code 128 credential. */
export type AuthorizationBody = { userName: string; password: string } | { credentialToken: string };
