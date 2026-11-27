# Auditoría ISO IEC SQuaRE de ControlPlus

## Línea base del 27 de noviembre de 2026

Esta auditoría inicia la evaluación continua de ControlPlus con las normas ISO/IEC 25010:2023,
ISO/IEC 25019:2023, ISO/IEC 25030:2019, ISO/IEC 25040:2024 e ISO/IEC 25023:2016. Su objetivo
es separar tres estados: cumplimiento acreditado, cumplimiento parcial y falta de evidencia. La falta
de una medición no se presenta como un defecto del sistema, pero permanece abierta hasta obtener una
evidencia reproducible.

El alcance de esta primera revisión es el árbol local de `main` sobre `9fc5e54`. Al iniciar existían
modificaciones concurrentes en reportes, documentos PDF, datos del establecimiento y el instalador;
se conservaron y luego quedaron confirmadas por su responsable en ese commit. Esta auditoría no las
preparó como propias. No se accedió a la base persistente `controlplus`, no se aplicaron migraciones y
no se solicitaron ni registraron secretos.

## Método de evaluación

La evaluación sigue el orden operativo de la familia SQuaRE:

1. ISO/IEC 25030: identificar necesidades, requisitos, contexto, medida y criterio de aceptación.
2. ISO/IEC 25010: clasificar la calidad técnica del producto en sus nueve características.
3. ISO/IEC 25019: evaluar el resultado del uso por Cajero, Supervisor y Administrador.
4. ISO/IEC 25023: obtener medidas cuantitativas repetibles.
5. ISO/IEC 25040: conservar alcance, evidencia, hallazgos, correcciones y conclusiones.

ISO/IEC 25023:2016 continúa siendo la edición publicada, pero está en revisión y será sustituida
por ISO/IEC 25000-23.2. Mientras se publica la nueva edición, esta auditoría usa medidas de 25023:2016
y añade medidas propias para cubrir el modelo de nueve características de 25010:2023.

## Evidencia de la línea base

| Evidencia | Resultado | Alcance y límite |
| --- | --- | --- |
| `dotnet build ControlPlus.Backend.slnx -m:1 /warnaserror` con artefactos aislados | Aprobado, 0 advertencias y 0 errores | El intento normal quedó bloqueado por un `testhost` ajeno; la repetición usó `%TEMP%/ControlPlus-IsoAudit-*` sin detenerlo |
| Pruebas .NET con `Category!=Integration` | 402/402 aprobadas: Domain 100, Infrastructure 49, API 253 | No incluye PostgreSQL ni migraciones |
| `npx ng test --watch=false` | 858/858 aprobadas en 92 archivos | Pruebas de componentes y servicios Angular; no sustituye la observación de usuarios reales |
| `npx ng build` | Aprobado | Conserva advertencias de presupuesto en `cash.scss` (+356 bytes) y `sales.scss` (+856 bytes) |
| `dotnet list ... package --vulnerable --include-transitive` | 0 paquetes vulnerables en los 7 proyectos | Consulta contra NuGet; refleja los avisos publicados en ese momento |
| `npm audit --omit=dev` | 0 vulnerabilidades en 15 dependencias de producción | La aplicación instalada no incluía el hallazgo de la herramienta de desarrollo |
| `npm audit` completo | 0 vulnerabilidades después de actualizar Angular a 22.2.5 y Vite a 8.3.5 | Antes de la corrección detectó 1 aviso alto y 1 bajo en dependencias de desarrollo |
| CI de `2eccf5b` | Aprobado, ejecución `45945427738` | Acredita el commit; no incluye modificaciones locales sin confirmar |
| SonarQube de `b0a4962` | Quality Gate OK, cobertura nueva 85,0 %, duplicación nueva 0,90667 %, 0 infracciones nuevas | Es la última medición publicada, pero precede a los cambios posteriores y no representa el árbol local actual |
| Playwright del commit `84d688b` | 11/11 recorridos en CI | No acredita las modificaciones locales posteriores |

## Matriz ISO IEC 25010 de calidad del producto

| Característica | Evidencia actual | Estado | Acción siguiente |
| --- | --- | --- | --- |
| Adecuación funcional | Pruebas de dominio, API, Angular, integración histórica y 11 recorridos E2E | Parcialmente acreditada | Repetir PostgreSQL y E2E sobre un commit limpio que contenga las correcciones actuales |
| Eficiencia de desempeño | Objetivo general de respuesta de hasta un segundo; sin prueba automática de tiempos o carga encontrada | Sin evidencia suficiente | Definir muestra, equipo, volumen, percentil y umbral; medir búsquedas, venta y cierre de caja |
| Compatibilidad | Build local Windows, CI Linux y navegador Chromium | Parcialmente acreditada | Registrar matriz Windows, Chrome y Edge; comprobar impresión y lector en el equipo de la tienda |
| Capacidad de interacción | 858 pruebas Angular y 11 recorridos Playwright | Parcialmente acreditada | Ejecutar tareas observadas con Cajero, Supervisor y Administrador reales |
| Fiabilidad | Pruebas de transacciones, concurrencia, idempotencia y conservación de originales documentadas | Parcialmente acreditada | Añadir ensayo de recuperación ante interrupción y restauración verificable de respaldo |
| Seguridad | JWT, permisos efectivos, bloqueo de cuentas, auditoría, encabezados HTTP y auditoría de dependencias sin vulnerabilidades conocidas | Parcialmente acreditada | Actualizar SonarQube y ampliar pruebas negativas de sesiones |
| Mantenibilidad | Arquitectura por capas, build con advertencias como error, 402 pruebas .NET y Quality Gate previo | Acreditada con observaciones | Actualizar SonarQube y resolver los dos presupuestos de estilos sin cambiar el prototipo |
| Flexibilidad | Configuraciones de negocio, migraciones controladas y operación local | Parcialmente acreditada | Probar cambio de configuración, reinstalación y operación sin Internet con criterios medibles |
| Seguridad frente a daños | Invariantes financieros, auditoría, idempotencia y correcciones que conservan originales | Parcialmente acreditada | Crear una matriz de riesgos monetarios e inventario con severidad, prevención y recuperación |

## Calidad en uso ISO IEC 25019

| Característica | Evidencia | Estado |
| --- | --- | --- |
| Beneficio obtenido | Los recorridos E2E completan venta, crédito, caja, apartado, compra y correcciones | Parcialmente acreditada; falta validar que representen las tareas reales y su frecuencia |
| Ausencia de riesgo | Permisos, bloqueos, transacciones, historial e idempotencia | Parcialmente acreditada; falta la matriz consolidada de riesgos y consecuencias |
| Aceptabilidad | Pruebas automáticas de interfaz | Sin evidencia de usuario real; las pruebas automáticas no miden comprensión, satisfacción ni necesidad de ayuda |

## Requisitos medibles ISO IEC 25030 y 25023

La siguiente plantilla es obligatoria para nuevos requisitos de calidad y para convertir reglas ya
existentes en criterios auditables:

| Campo | Contenido requerido |
| --- | --- |
| Identificador | `QR-<módulo>-<número>` estable |
| Necesidad | Resultado que necesita el negocio o el usuario |
| Contexto | Rol, equipo, datos, estado de caja, red y base utilizados |
| Característica | Característica y subcaracterística ISO relacionada |
| Medida | Fórmula, unidad, tamaño de muestra y tratamiento de errores |
| Criterio | Umbral de aprobación definido antes de ejecutar la prueba |
| Evidencia | Prueba, TRX, LCOV, captura, traza o registro reproducible |
| Resultado | Cumple, no cumple, parcial o no evaluado |
| Corrección | Cambio vinculado y prueba de regresión |

Requisitos iniciales pendientes de medición:

| Identificador | Requisito propuesto | Estado |
| --- | --- | --- |
| QR-PERF-001 | El 95 % de las búsquedas de producto responde en hasta 1 segundo bajo un volumen y equipo definidos | Falta fijar volumen, equipo y prueba |
| QR-REL-001 | Ninguna operación financiera confirmada queda parcialmente persistida ante un fallo controlado | Cubierto por varios casos de integración; falta inventario consolidado |
| QR-SEC-001 | Toda respuesta HTTP incluye protecciones básicas contra inferencia de tipo, marcos y capacidades no usadas | Corregido y probado en una respuesta correcta y una solicitud no autorizada |
| QR-SEC-002 | Las dependencias de producción y construcción no tienen vulnerabilidades conocidas por NuGet o npm | Cumple en la consulta del 27 de noviembre de 2026 |
| QR-USE-001 | Cada rol completa sus tareas críticas sin ayuda y dentro del tiempo acordado | No evaluado con usuarios reales |
| QR-REC-001 | Un respaldo se restaura en un tiempo máximo acordado sin pérdida de la muestra verificada | Falta definir RTO, RPO y ensayo |

## Hallazgos y correcciones

### ISO-AUD-001 trazabilidad dispersa

- Clasificación: ISO/IEC 25030 e ISO/IEC 25040.
- Estado inicial: no existía una matriz única que relacionara normas, evidencia, brechas y correcciones.
- Corrección: este documento establece la línea base, el método, la plantilla y los identificadores.
- Estado: corregido documentalmente; la matriz debe mantenerse en cada corte.

### ISO-AUD-002 encabezados de protección del navegador ausentes

- Clasificación: seguridad, ISO/IEC 25010:2023.
- Evidencia: la canalización configuraba HTTPS y límites de solicitudes, pero no enviaba encabezados base.
- Corrección: se añadieron `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
  `Referrer-Policy: no-referrer` y una política que deshabilita cámara, micrófono y geolocalización.
- Límite: no se añadió una política CSP sin validar primero Angular, impresión y recursos de la pantalla.
- Prueba: `SecurityHeadersTests.Responses_IncludeBaselineBrowserSecurityHeaders`, incluida en las 402 pruebas
  .NET sin integración aprobadas; verifica una respuesta `200` y una `401` de la política global.
- Estado: corregido y validado localmente.

### ISO-AUD-003 evidencia de rendimiento ausente

- Clasificación: eficiencia de desempeño, ISO/IEC 25010:2023 e ISO/IEC 25023:2016.
- Evidencia: no se encontraron pruebas que midan latencia, percentiles o carga.
- Estado: abierto; no se concluye que el sistema sea lento.

### ISO-AUD-004 aceptabilidad sin usuarios reales

- Clasificación: aceptabilidad, ISO/IEC 25019:2023.
- Evidencia: existen pruebas Angular y E2E, pero no observación documentada de usuarios reales.
- Estado: abierto; requiere sesiones con los tres roles sin compartir contraseñas con el agente.

### ISO-AUD-005 evidencia publicada desactualizada

- Clasificación: evaluación y medición, ISO/IEC 25040 e ISO/IEC 25023.
- Evidencia: el último SonarQube publicado corresponde a `b0a4962`, y los 11 recorridos E2E a `84d688b`.
- Estado: abierto; debe repetirse sobre un commit limpio después de cerrar los cambios concurrentes.

### ISO-AUD-006 presupuestos de estilos

- Clasificación: eficiencia y mantenibilidad, ISO/IEC 25010:2023.
- Evidencia: `cash.scss` supera el presupuesto por 356 bytes y `sales.scss` por 856 bytes.
- Corrección (27 de noviembre de 2026): cada hoja se dividió en dos archivos que el componente carga en el mismo
  orden (`cash.scss` + `cash-panels.scss`, `sales.scss` + `sales-checkout.scss`), cortando entre reglas completas.
  Se comprobó que las dos partes juntas son idénticas al archivo anterior, así que la apariencia no cambia; no se
  aumentó el presupuesto.
- Validación: build de producción sin advertencias de presupuesto y Angular 860/860.
- Estado: corregido y validado localmente.

### ISO-AUD-007 Vite vulnerable en la cadena de desarrollo

- Clasificación: seguridad y mantenibilidad, ISO/IEC 25010:2023.
- Evidencia inicial: `npm audit` detectó 1 aviso alto y 1 bajo en dependencias de desarrollo;
  Vite 8.3.0 era transitivo de `@angular/build` 22.2.4. Las 15 dependencias de producción
  conservaron 0 vulnerabilidades.
- Corrección: se fijó la familia Angular en 22.2.5, que resuelve Vite 8.3.5, y se regeneró
  `package-lock.json` sin opciones `--force` ni `--legacy-peer-deps`.
- Validación: `npm audit` completo con 0 vulnerabilidades, Angular 858/858 y build de producción aprobado.
- Estado: corregido y validado localmente.

## Criterio de cierre de la auditoría

Una característica solo se marcará como acreditada cuando tenga requisito, contexto, medida, umbral,
ejecución reproducible y evidencia del mismo commit. Una corrección requiere una prueba que falle antes
o demuestre la ausencia observada, pase después y no invalide las suites relacionadas. SonarQube, CI,
PostgreSQL, Playwright y pruebas con usuarios se informarán por separado para no atribuir a un nivel la
evidencia de otro.
