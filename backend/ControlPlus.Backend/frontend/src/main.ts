import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { applyDisplayScale, readStoredScale } from './app/core/display-scale';

// El tamaño elegido en este equipo se aplica antes de dibujar la primera pantalla.
applyDisplayScale(readStoredScale());

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
