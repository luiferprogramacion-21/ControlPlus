# Auditoría ISO IEC SQuaRE de ControlPlus

## Línea base del 27 de noviembre de 2026

Esta auditoría inicia la evaluación continua de ControlPlus con las normas ISO/IEC 25010:2023,
ISO/IEC 25019:2023, ISO/IEC 25030:2019, ISO/IEC 25040:2024 e ISO/IEC 25023:2016. Su objetivo
es separar tres estados: cumplimiento acreditado, cumplimiento parcial y falta de evidencia. La falta
de una medición no se presenta como un defecto del sistema, pero permanece abierta hasta obtener una
evidencia reproducible.

El alcance de esta primera revisión quedó confirmado en el commit de código `1bfad86` y se concilió después
con la evidencia operativa y el código integrados hasta `351f988`. Al iniciar existían
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
| `npx ng test --watch=false` | 862/862 aprobadas en 92 archivos sobre `351f988` | Pruebas de componentes y servicios Angular; no sustituye la observación de usuarios reales |
| `npx ng build` | Aprobado sin advertencias de presupuesto sobre `351f988` | La línea base tenía advertencias en `cash.scss` (+356 bytes) y `sales.scss` (+856 bytes); ambas se corrigieron sin cambiar la pantalla, según `ISO-AUD-006` |
| `dotnet list ... package --vulnerable --include-transitive` | 0 paquetes vulnerables en los 7 proyectos | Consulta contra NuGet; refleja los avisos publicados en ese momento |
| `npm audit --omit=dev` | 0 vulnerabilidades en 15 dependencias de producción | La aplicación instalada no incluía el hallazgo de la herramienta de desarrollo |
| `npm audit` completo | 0 vulnerabilidades después de actualizar Angular a 22.2.5 y Vite a 8.3.5 | Antes de la corrección detectó 1 aviso alto y 1 bajo en dependencias de desarrollo |
| CI de `1bfad86` | Aprobado, ejecución `46020622376` | Frontend, backend sin base, backend con PostgreSQL 18 y navegador aprobados |
| CI del código vigente en `351f988` | Aprobado, ejecución `46056425621` | Los cuatro trabajos aprobaron: frontend, backend sin base, backend con PostgreSQL 18 y navegador |
| SonarQube de `b0a4962` | Quality Gate OK, cobertura nueva 85,0 %, duplicación nueva 0,90667 %, 0 infracciones nuevas | Es la última medición publicada, pero precede a los cambios posteriores y no representa el árbol local actual |
| Playwright del commit `351f988` | Trabajo de navegador aprobado en el CI final | Acredita los recorridos vigentes del repositorio contra una API y PostgreSQL descartables |
| Simulacro real de recuperación del 27 de noviembre | `RECOVERY_DRILL_OK`: restauración en 1 minuto y copia de 23 minutos de antigüedad, ambas dentro de la meta de 30 minutos | La copia cifrada coincidió con 39 migraciones, 26 productos, 103 ventas y la última venta; la base temporal se eliminó y no se reemplazó `controlplus`. No acredita todavía la instalación en otro equipo |
| Pruebas reales en la tienda aprobadas por el usuario | Turno compartido, identificación del operador, cajón en cobro y abono, cierre propio autorizado, impresión térmica y desbloqueo con carné/lector | Acredita dispositivos y flujos en el entorno real; no equivale a una sesión de aceptabilidad observada con empleados de los tres roles |

## Matriz ISO IEC 25010 de calidad del producto

| Característica | Evidencia actual | Estado | Acción siguiente |
| --- | --- | --- | --- |
| Adecuación funcional | Pruebas de dominio, API, Angular y recorridos E2E; el CI de `351f988` aprobó PostgreSQL 18 y navegador | Parcialmente acreditada | Mantener la trazabilidad entre requisitos, flujos secundarios/terciarios y pruebas de regresión |
| Eficiencia de desempeño | Objetivo general de respuesta de hasta un segundo; sin prueba automática de tiempos o carga encontrada | Sin evidencia suficiente | Definir muestra, equipo, volumen, percentil y umbral; medir búsquedas, venta y cierre de caja |
| Compatibilidad | Build local Windows, CI Linux, Chromium, impresora térmica, cajón y lector comprobados en la tienda | Parcialmente acreditada | Registrar Edge y ensayar la instalación/recuperación en otro equipo cuando exista uno disponible |
| Capacidad de interacción | Pruebas Angular y Playwright, más flujos reales aprobados por el usuario en la tienda | Parcialmente acreditada | Ejecutar tareas observadas con empleados Cajero, Supervisor y Administrador y registrar ayuda, errores y tiempo |
| Fiabilidad | Pruebas transaccionales y simulacro real de respaldo con RPO/RTO dentro de 30 minutos | Parcialmente acreditada | Ensayar recuperación en otro equipo y fallos controlados de servicio/energía sin tocar la base real |
| Seguridad | JWT, permisos efectivos, bloqueo de cuentas, auditoría, encabezados HTTP y auditoría de dependencias sin vulnerabilidades conocidas | Parcialmente acreditada | Actualizar SonarQube y ampliar pruebas negativas de sesiones |
| Mantenibilidad | Arquitectura por capas, build con advertencias como error, 402 pruebas .NET, presupuestos de estilos corregidos y Quality Gate previo | Acreditada con observaciones | Actualizar SonarQube sobre el código vigente |
| Flexibilidad | Configuraciones de negocio, migraciones controladas y operación local | Parcialmente acreditada | Probar cambio de configuración, reinstalación y operación sin Internet con criterios medibles |
| Seguridad frente a daños | Invariantes financieros, auditoría, idempotencia y correcciones que conservan originales | Parcialmente acreditada | Crear una matriz de riesgos monetarios e inventario con severidad, prevención y recuperación |

## Calidad en uso ISO IEC 25019

| Característica | Evidencia | Estado |
| --- | --- | --- |
| Beneficio obtenido | Los recorridos E2E completan venta, crédito, caja, apartado, compra y correcciones; además se aprobaron flujos de caja, impresión y carné en la tienda | Parcialmente acreditada; falta medir frecuencia, tiempo y éxito con cada rol real |
| Ausencia de riesgo | Permisos, bloqueos, transacciones, historial e idempotencia | Parcialmente acreditada; falta la matriz consolidada de riesgos y consecuencias |
| Aceptabilidad | Pruebas automáticas y aprobación del usuario de varios flujos en la tienda | Parcialmente acreditada; falta observar empleados de los tres roles y medir comprensión, satisfacción y necesidad de ayuda |

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
| QR-USE-001 | Cada rol completa sus tareas críticas sin ayuda y dentro del tiempo acordado | Parcial: el usuario aprobó flujos reales en la tienda, pero faltan empleados de los tres roles y tiempos acordados |
| QR-REC-001 | Un respaldo se restaura en un tiempo máximo acordado sin pérdida de la muestra verificada | Cumple en el simulacro del 27 de noviembre: recuperación 1/30 minutos y antigüedad 23/30 minutos; queda pendiente otro equipo |

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
- Evidencia inicial: existían pruebas Angular y E2E, pero no observación documentada en la tienda.
- Evidencia posterior: el usuario aprobó turno compartido, identificación de operador, apertura del cajón,
  cierre propio autorizado, impresión térmica y desbloqueo con carné y lector en el entorno real.
- Estado: parcialmente acreditado; sigue abierta la sesión observada con empleados de los tres roles,
  sin compartir contraseñas con el agente, para medir tiempo, errores, ayuda y satisfacción.

### ISO-AUD-005 evidencia publicada desactualizada

- Clasificación: evaluación y medición, ISO/IEC 25040 e ISO/IEC 25023.
- Evidencia: el último SonarQube publicado corresponde a `b0a4962`; en cambio, el CI de `351f988`
  aprobó los trabajos actuales de PostgreSQL 18 y Playwright.
- Estado: abierto solo para actualizar la medición de SonarQube sobre un commit vigente.

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
