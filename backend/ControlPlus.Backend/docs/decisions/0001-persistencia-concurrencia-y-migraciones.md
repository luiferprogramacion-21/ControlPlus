# ADR 0001: modelo oficial, concurrencia y migraciones

- Estado: aceptada.
- Fecha: 2026-09-13.

## Contexto

ControlPlus conserva un script PostgreSQL, MER y diccionario oficiales de Fase 4. El modelo runtime se generó desde esa fuente y las migraciones iniciales ya fueron publicadas y aplicadas. La auditoría detectó que los campos oficiales `version` requerían concurrencia optimista real y que la cadena histórica no puede alterarse de forma retroactiva.

## Decisiones

1. `OfficialControlPlusDbContext` es la única fuente de verdad del modelo persistente en ejecución. `ControlPlusDbContext` queda limitado a portar la asociación con la cadena histórica de migraciones; no se registra para los repositorios runtime.
2. Las 27 entidades oficiales que tienen `version` como token en el diccionario se configuran centralmente con `IsConcurrencyToken()`. `OfficialControlPlusDbContext` incrementa la versión desde el valor original una vez por guardado y EF Core compara el valor original al actualizar. Un conflicto se convierte en `409` con código `concurrency.conflict`.
3. Las cuatro migraciones publicadas son inmutables: `20260912010000_OfficialPhase4Baseline`, `20260912011000_SeedApprovedSecurityCatalog`, `20260912012000_HybridPermissionsV1` y `20260913010000_SeedMeasurementUnitsV1`. En particular, `HybridPermissionsV1.Down` no es una reversión completa y segura.
4. Las correcciones futuras se implementan mediante migraciones compensatorias nuevas, no editando la historia. Cada una se revisa contra el modelo oficial, se prueba creando una base limpia con Testcontainers y solo después se considera para PostgreSQL persistente. Antes de una aplicación o reversión persistente se exige un respaldo local verificado fuera del repositorio.
5. No se crea un `ModelSnapshot` parcial o artificial. Si se incorpora una estrategia de snapshot o generación de migraciones por EF Core, el snapshot deberá representar fielmente el modelo oficial completo y demostrarlo mediante una creación limpia de la base.

## Consecuencias

- Las actualizaciones concurrentes de usuarios, roles, categorías, productos y las entidades oficiales versionadas no se sobrescriben silenciosamente.
- Los consumidores de la API reciben un conflicto estable y seguro que pueden resolver recargando el recurso.
- La trazabilidad del esquema se conserva incluso cuando una decisión posterior modifica semillas o permisos aprobados.
- Las futuras migraciones requieren disciplina de revisión, pruebas aisladas y respaldo, pero no introducen cambios manuales no documentados en PostgreSQL.

La arquitectura y la operación práctica se describen en [architecture.md](../architecture.md) y [database.md](../database.md).
