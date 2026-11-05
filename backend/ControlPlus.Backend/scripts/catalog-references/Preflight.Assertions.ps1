# Pure report assertions. No executable, database or credential is accessed on loading.
$CatalogRequiredPredecessors = @(
 '20260912010000_OfficialPhase4Baseline',
 '20260912011000_SeedApprovedSecurityCatalog',
 '20260912012000_HybridPermissionsV1',
 '20260913010000_SeedMeasurementUnitsV1',
 '20260913020000_CashRegisterModuleV1',
 '20260930010000_SeedInventoryAdjustmentReasonsV1',
 '20261005010000_CustomerSearchIndexesV1',
 '20261014010000_InventoryMovementRegistrationOrderV1',
 '20261020010000_LayawayDeliveryWithoutSaleV1',
 '20261021010000_LayawayCancellationOptionalReasonV1',
 '20261026010000_InitialProductInventoryMovementV1',
 '20261028010000_SaleReceiptDataSnapshotV1'
)

function ConvertFrom-CatalogPreflightReport {
    param([string[]]$Lines)
    $report=@{}
    $required=@('target','connections','history','columns','constraints','reserved','sideEffects','data')
    try {
        foreach ($line in $Lines) {
            $row=ConvertFrom-Json -InputObject $line -ErrorAction Stop
            if ($required -cnotcontains [string]$row.kind -or $report.ContainsKey([string]$row.kind)) {
                throw 'Tipo de registro inesperado o repetido.'
            }
            $report[[string]$row.kind]=$row
        }
        if ($report.Count -ne $required.Count) { throw 'Informe incompleto.' }
    } catch { throw 'Informe de lectura incompleto o invalido; no continuar.' }
    return ,$report
}

function Get-CatalogPreflightBlockers {
    param([hashtable]$Report,[string]$ExpectedUser)
    $issues=[System.Collections.Generic.List[string]]::new()
    $t=$Report.target
    if ($t.server -cne '127.0.0.1' -or [int]$t.port -ne 5432 -or $t.database -cne 'controlplus' -or
        $t.username -cne $ExpectedUser -or $t.sessionUser -cne $ExpectedUser) {
        $issues.Add('Destino o usuario efectivo distintos de los configurados.')
    }
    if ($t.readOnly -cne 'on' -or $t.isolation -cne 'repeatable read') { $issues.Add('Lectura aislada no acreditada.') }
    if ([int]$t.serverVersion -lt 180000 -or [int]$t.serverVersion -gt 189999) { $issues.Add('Se requiere PostgreSQL 18.') }
    if (-not $Report.connections.fullVisibility) {
        $issues.Add('Visibilidad de conexiones parcial: no se acredita ausencia de otros clientes.')
    }
    if ([long]$Report.connections.visibleOtherClients -ne 0 -or [long]$Report.connections.unclassifiedOtherSessions -ne 0) {
        $issues.Add('Hay otros clientes visibles o sesiones sin clasificar; revisar ventana de mantenimiento.')
    }
    $history=@($Report.history.items)
    if (-not $Report.history.readable) { $issues.Add('Historial no disponible con la forma esperada.') }
    $exact=($history.Count -eq $CatalogRequiredPredecessors.Count)
    if ($exact) {
        for ($i=0;$i -lt $history.Count;$i++) {
            if (-not [string]::Equals([string]$history[$i],$CatalogRequiredPredecessors[$i],[StringComparison]::Ordinal)) { $exact=$false }
        }
    }
    if (-not $exact) { $issues.Add('El historial completo no coincide con las doce predecesoras exactas.') }
    if ($history -cnotcontains '20261028010000_SaleReceiptDataSnapshotV1') {
        $issues.Add('Falta SaleReceiptDataSnapshotV1: revisar y autorizar por separado; no aplicar predecesoras automaticamente.')
    }
    if ($history -ccontains '20261029010000_PermanentProductReferencesV1') {
        $issues.Add('La migracion objetivo ya figura aplicada; no repetirla.')
    }
    if (-not $Report.columns.productExists -or @($Report.columns.mismatches).Count -ne 0) {
        $issues.Add('Tabla o columnas de producto incompatibles con el contrato.')
    }
    if (@($Report.constraints.keyMismatches).Count -ne 0 -or -not $Report.constraints.formatValidated -or
        -not $Report.constraints.nonBlankCodeChecksValidated) { $issues.Add('Claves o restricciones de producto incompatibles/no validadas.') }
    if (@($Report.reserved.present).Count -ne 0) { $issues.Add('Hay objetos reservados del objetivo: revisar estado/posible aplicacion parcial.') }
    if ([long]$Report.sideEffects.enabledProductTriggers -ne 0 -or [long]$Report.sideEffects.productRules -ne 0) {
        $issues.Add('Triggers o reglas existentes de producto requieren revision por sus efectos.')
    }
    if (-not $Report.data.readable) { $issues.Add('No se pudo evaluar la compatibilidad de los datos de producto.') }
    elseif ([long]$Report.data.ambiguousCodeGroups -ne 0 -or [long]$Report.data.versionsAtMaximum -ne 0) {
        $issues.Add('Codigos ambiguos o versiones que desbordarian durante la transicion.')
    }
    return $issues.ToArray()
}
