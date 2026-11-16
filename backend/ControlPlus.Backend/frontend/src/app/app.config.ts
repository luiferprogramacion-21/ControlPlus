import { ApplicationConfig, LOCALE_ID, provideBrowserGlobalErrorListeners } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { authInterceptor } from './core/api';
import { routes } from './app.routes';

// Importes y fechas en formato colombiano ($ 10.000), como en el prototipo.
registerLocaleData(localeEsCo);

export const appConfig: ApplicationConfig = {
  providers: [provideBrowserGlobalErrorListeners(), provideHttpClient(withInterceptors([authInterceptor])), provideRouter(routes), { provide: LOCALE_ID, useValue: 'es-CO' }],
};
