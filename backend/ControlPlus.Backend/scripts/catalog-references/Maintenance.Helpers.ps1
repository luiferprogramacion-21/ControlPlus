# Review-only helpers for Windows PowerShell 5.1. Loading this file opens no connection.
$CatalogReferenceTarget = '20261029010000_PermanentProductReferencesV1'

function ConvertTo-CatalogNativeArgument {
    param([AllowEmptyString()][string]$Value)
    # CommandLineToArgvW rules; ProcessStartInfo.ArgumentList is unavailable in PS 5.1.
    $escaped = [regex]::Replace($Value,'(\\*)"','$1$1\"')
    $escaped = [regex]::Replace($escaped,'(\\+)$','$1$1')
    return '"' + $escaped + '"'
}

function Get-CatalogSafeToolDiagnostic {
    param([string]$Stage,[string]$Tool,[string]$Phase,[object]$ExitCode,
        [string]$ErrorText,[Exception]$Failure)
    # Allow only codes and numeric positions. Never retain provider message/detail/query.
    $states=@([regex]::Matches($ErrorText,'(?im)(?:ERROR|FATAL|PANIC|SQLSTATE)[ \t]*:[ \t]*((?:[0-9][0-9A-Z]|F0|HV|P0|XX)[0-9A-Z]{3})[ \t]*\r?$') |
        ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
    $lines=@([regex]::Matches($ErrorText,'(?im)^psql:.*:([0-9]+):\s*(?:ERROR|FATAL|PANIC):') |
        ForEach-Object { [long]$_.Groups[1].Value } | Select-Object -Unique)
    $nativeError=$null
    $cause=$Failure
    while ($null -ne $cause) {
        if ($cause -is [System.ComponentModel.Win32Exception]) { $nativeError=$cause.NativeErrorCode; break }
        $cause=$cause.InnerException
    }
    # These fixed categories are emitted only when the client text matches.
    # Do not copy the matching text, account, host, data values or connection string.
    $category='Unclassified'
    if ($ErrorText -match '(?i)password authentication failed|autenti(?:ca|fi)ca(?:tion|ci.n).*password.*fall') { $category='AuthenticationRejected' }
    elseif ($ErrorText -match '(?i)no pg_hba\.conf entry') { $category='HbaRuleMissing' }
    elseif ($ErrorText -match '(?i)connection refused|conexi.n rechazada') { $category='ConnectionRefused' }
    elseif ($ErrorText -match '(?i)no password supplied') { $category='PasswordRequired' }
    return [pscustomobject][ordered]@{
        utc=[DateTime]::UtcNow.ToString('o'); stage=$Stage; tool=$Tool; phase=$Phase
        exitCode=$ExitCode; sqlStates=$states; sqlLines=$lines; nativeErrorCode=$nativeError
        hresult=$Failure.HResult; clientCategory=$category
    }
}

function Invoke-CatalogNativeProcess {
    param([string]$Program,[string[]]$NativeArguments,[hashtable]$EnvironmentOverrides=@{},
        [string[]]$RemoveEnvironment=@(),
        [ValidatePattern('^[A-Za-z0-9_.-]{1,80}$')][string]$Stage='NativeProcess',
        [ValidateSet('native','psql','pg_dump','pg_restore','createdb','dropdb','dotnet')][string]$Tool='native')
    if (-not (Test-Path -LiteralPath $Program -PathType Leaf)) { throw 'Ejecutable ausente.' }
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName=$Program
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $info.StandardOutputEncoding=[System.Text.Encoding]::UTF8
    $info.StandardErrorEncoding=[System.Text.Encoding]::UTF8
    $quoted = @($NativeArguments | ForEach-Object { ConvertTo-CatalogNativeArgument ([string]$_) })
    $info.Arguments=[string]::Join(' ',[string[]]$quoted)
    foreach ($name in @($info.EnvironmentVariables.Keys)) {
        if ($RemoveEnvironment -contains $name) { $info.EnvironmentVariables.Remove($name) }
    }
    foreach ($name in $EnvironmentOverrides.Keys) { $info.EnvironmentVariables[$name]=[string]$EnvironmentOverrides[$name] }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo=$info
    $phase='Start'; $exitCode=$null; $errorText=''
    try {
        if (-not $process.Start()) { throw 'No se pudo iniciar la herramienta.' }
        $phase='CaptureOutput'
        $stdout=$process.StandardOutput.ReadToEndAsync()
        $stderr=$process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $text=$stdout.GetAwaiter().GetResult()
        $errorText=$stderr.GetAwaiter().GetResult()
        $exitCode=$process.ExitCode
        $phase='Exited'
        if ($exitCode -ne 0) { throw 'Salida de herramienta distinta de cero.' }
        return @($text -split '\r?\n' | Where-Object { $_ -ne '' })
    } catch {
        $diagnostic=Get-CatalogSafeToolDiagnostic -Stage $Stage -Tool $Tool -Phase $phase `
            -ExitCode $exitCode -ErrorText $errorText -Failure $_.Exception
        $safe=ConvertTo-Json -InputObject $diagnostic -Compress -Depth 4
        $failure=[InvalidOperationException]::new('La herramienta no completo la fase. Diagnostico seguro: '+$safe)
        $failure.Data['CatalogDiagnostic']=$diagnostic
        throw $failure
    } finally {
        $process.Dispose()
        $info.EnvironmentVariables.Clear()
        $errorText=$null
    }
}

function Assert-CatalogDatabase([string]$Database) {
    if ($Database -ne 'controlplus' -and $Database -ne 'postgres' -and
        $Database -cnotmatch '^controlplus_test_[a-f0-9]{32}$') {
        throw 'Destino de base rechazado.'
    }
}

function Invoke-CatalogPgTool {
    param([string]$PgBin, [ValidateSet('psql','pg_dump','pg_restore','createdb','dropdb')][string]$Tool,
        [pscredential]$Credential, [string[]]$PgArguments, [switch]$ReadOnly,
        [ValidatePattern('^[A-Za-z0-9_.-]{1,80}$')][string]$Stage='PostgresTool')
    $program = Join-Path $PgBin ($Tool + '.exe')
    if (-not (Test-Path -LiteralPath $program -PathType Leaf)) { throw 'Herramienta PostgreSQL ausente.' }
    $child = @{ PGPASSWORD=$Credential.GetNetworkCredential().Password; PGCLIENTENCODING='UTF8'; PGCONNECT_TIMEOUT='10'; PGOPTIONS='-c timezone=UTC' }
    if ($ReadOnly) { $child.PGOPTIONS += ' -c default_transaction_read_only=on' }
    try {
        $nativeArgs = @('-h','127.0.0.1','-p','5432','-U',$Credential.UserName,'-w') + $PgArguments
        # libpq PGHOSTADDR can override the network address despite an explicit -h.
        # Service/passfile settings must not supply an alternative destination or secret.
        return Invoke-CatalogNativeProcess -Program $program -NativeArguments $nativeArgs -EnvironmentOverrides $child `
            -RemoveEnvironment @('PGHOSTADDR','PGSERVICE','PGSERVICEFILE','PGPASSFILE') -Stage $Stage -Tool $Tool
    } finally {
        $child.Clear()
    }
}

function Invoke-CatalogSqlFile {
    param([string]$PgBin,[pscredential]$Credential,[string]$Database,[string]$SqlFile,
        [ValidatePattern('^[A-Za-z0-9_.-]{1,80}$')][string]$Stage='SqlFile')
    Assert-CatalogDatabase $Database
    if (-not (Test-Path -LiteralPath $SqlFile -PathType Leaf)) { throw 'Archivo SQL ausente.' }
    Invoke-CatalogPgTool -PgBin $PgBin -Tool psql -Credential $Credential -ReadOnly -Stage $Stage -PgArguments @(
        '-X','-q','-A','-t','-P','pager=off','-v','ON_ERROR_STOP=1','-v','VERBOSITY=sqlstate','-d',$Database,'-f',$SqlFile)
}

function Save-CatalogSnapshot {
    param([string]$PgBin,[pscredential]$Credential,[string]$Database,[string]$SqlDirectory,[string]$Path)
    if (Test-Path -LiteralPath $Path) { throw 'La instantanea ya existe; no sobrescribir evidencia.' }
    $lines = @(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $Credential -Database $Database `
        -SqlFile (Join-Path $SqlDirectory 'Snapshot.sql'))
    if ($lines.Count -eq 0) { throw 'Instantanea vacia.' }
    foreach ($line in $lines) {
        $row = ConvertFrom-Json -InputObject ([string]$line)
        if (-not $row.kind -or -not $row.key) { throw 'Registro de instantanea invalido.' }
    }
    [System.IO.File]::WriteAllLines($Path,[string[]]$lines,[System.Text.UTF8Encoding]::new($false))
}

function Read-CatalogSnapshot([string]$Path) {
    $map = [System.Collections.Generic.Dictionary[string,object]]::new([System.StringComparer]::Ordinal)
    foreach ($line in [System.IO.File]::ReadAllLines($Path,[System.Text.Encoding]::UTF8)) {
        $row = ConvertFrom-Json -InputObject $line
        $key = ([string]$row.kind) + ':' + ([string]$row.key)
        if ($map.ContainsKey($key)) { throw 'Instantanea con claves duplicadas.' }
        $map.Add($key,$row)
    }
    # Prevent PowerShell from enumerating the dictionary as pipeline output.
    return ,$map
}

function ConvertTo-CatalogPgJsonString {
    param([string]$Value)
    # Match jsonb text string escaping, so raw Snapshot.sql digests remain verifiable.
    $b=[Text.StringBuilder]::new(); [void]$b.Append('"')
    foreach($c in $Value.ToCharArray()) {
        switch ([int]$c) {
            34 { [void]$b.Append('\"'); break }
            92 { [void]$b.Append('\\'); break }
            8 { [void]$b.Append('\b'); break }
            12 { [void]$b.Append('\f'); break }
            10 { [void]$b.Append('\n'); break }
            13 { [void]$b.Append('\r'); break }
            9 { [void]$b.Append('\t'); break }
            default {
                if ([int]$c -lt 32) { [void]$b.Append(('\u{0:x4}' -f [int]$c)) }
                else { [void]$b.Append($c) }
            }
        }
    }
    [void]$b.Append('"'); return $b.ToString()
}

function Get-CatalogRelationDigest {
    param([object[]]$Details,[switch]$CanonicalInventoryType)
    $items=[Collections.Generic.SortedDictionary[string,string]]::new([StringComparer]::Ordinal)
    $inventoryCheckSeen=$false
    foreach($r in $Details) {
        if ($null -eq $r -or @($r.PSObject.Properties).Count -ne 2 -or
            $r.item -isnot [string] -or $r.definition -isnot [string] -or
            $r.item -cnotmatch '^(constraint|index|trigger):[A-Za-z_][A-Za-z0-9_]*$' -or $items.ContainsKey($r.item)) {
            throw 'Relation details incomplete, duplicated or unsupported; cannot certify equivalence.'
        }
        $definition=[string]$r.definition
        if ($CanonicalInventoryType -and $r.item -ceq 'constraint:ck_movimiento_inventario_tipo') {
            # Narrow equivalence, not a general SQL rewriter. Other predicates remain byte-exact.
            $types=@('COMPRA','ANULACION_COMPRA','VENTA','ANULACION_VENTA','RESERVA_APARTADO',
                'LIBERACION_APARTADO','ENTREGA_APARTADO','CAMBIO_DEVOLUCION','CAMBIO_ENTREGA',
                'AJUSTE_ENTRADA','AJUSTE_SALIDA','REVERSO','INVENTARIO_INICIAL')
            $arrayCast="CHECK (tipo_movimiento::text = ANY (ARRAY["+
                (($types | ForEach-Object {"'"+$_+"'::character varying"}) -join ', ')+"]::text[]))"
            $elementCast="CHECK (tipo_movimiento::text = ANY (ARRAY["+
                (($types | ForEach-Object {"'"+$_+"'::character varying::text"}) -join ', ')+"]))"
            $parts=ConvertFrom-Json -InputObject $definition -ErrorAction Stop
            if ($parts -isnot [array] -or $parts.Count -ne 4 -or $parts[0] -isnot [string] -or
                $parts[1] -isnot [bool] -or $parts[2] -isnot [bool] -or $parts[3] -isnot [bool]) {
                throw 'Inventory CHECK metadata invalid; cannot certify equivalence.'
            }
            if ($parts[0] -ceq $arrayCast -or $parts[0] -ceq $elementCast) {
                # Keep all flags; only the two approved cast placements become identical.
                $parts[0]=$arrayCast
                $definition=ConvertTo-Json -InputObject $parts -Compress -Depth 4
                $inventoryCheckSeen=$true
            } else { throw 'Inventory CHECK expression unrecognized; cannot certify equivalence.' }
        }
        $items.Add($r.item,$definition)
    }
    if ($CanonicalInventoryType -and -not $inventoryCheckSeen) { throw 'Approved inventory CHECK missing; cannot certify equivalence.' }
    $b=[Text.StringBuilder]::new()
    foreach($pair in $items.GetEnumerator()) {
        [void]$b.Append('[');[void]$b.Append((ConvertTo-CatalogPgJsonString $pair.Key))
        [void]$b.Append(', ');[void]$b.Append((ConvertTo-CatalogPgJsonString $pair.Value));[void]$b.Append(']')
    }
    $sha=[Security.Cryptography.SHA256]::Create()
    try {return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($b.ToString())))).Replace('-','').ToLowerInvariant()}
    finally {$sha.Dispose()}
}

function Test-CatalogSnapshotRecordEqual {
    param($Before,$After)
    $inventoryShape=($Before.kind -ceq 'shape' -and $Before.key -ceq 'compras.movimiento_inventario' -and
                     $After.kind -ceq 'shape' -and $After.key -ceq 'compras.movimiento_inventario')
    if ($inventoryShape) {
        $hasBefore=$null -ne $Before.PSObject.Properties['relationDetails']
        $hasAfter=$null -ne $After.PSObject.Properties['relationDetails']
        if ($hasBefore -ne $hasAfter) {return $false} # Old/new formats require an explicit evidence bridge.
        if ($hasBefore) {
            foreach($row in @($Before,$After)) {
                if ($row.relationDetails -isnot [array] -or $row.relationDetails.Count -eq 0 -or
                    (Get-CatalogRelationDigest -Details $row.relationDetails) -cne $row.relationsHash) {
                    throw 'Relation detail digest does not match recorded raw relationsHash.'
                }
            }
            $a=[ordered]@{}; $b=[ordered]@{}
            foreach($p in $Before.PSObject.Properties) {if($p.Name -notin @('relationsHash','relationDetails')){$a[$p.Name]=$p.Value}}
            foreach($p in $After.PSObject.Properties) {if($p.Name -notin @('relationsHash','relationDetails')){$b[$p.Name]=$p.Value}}
            if ((ConvertTo-Json -InputObject $a -Compress -Depth 8) -cne
                (ConvertTo-Json -InputObject $b -Compress -Depth 8)) {return $false}
            return (Get-CatalogRelationDigest -Details $Before.relationDetails -CanonicalInventoryType) -ceq
                   (Get-CatalogRelationDigest -Details $After.relationDetails -CanonicalInventoryType)
        }
    }
    return (ConvertTo-Json -InputObject $Before -Compress -Depth 8) -ceq
           (ConvertTo-Json -InputObject $After -Compress -Depth 8)
}

function Assert-CatalogSnapshot {
    param([string]$Before,[string]$After,
        [ValidateSet('Restore','Upgrade','MigratedCopy')][string]$Mode)
    $old = Read-CatalogSnapshot $Before
    $new = Read-CatalogSnapshot $After
    $allowedNew = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $requiredNew = @('table:catalogo.producto_codigo','shape:catalogo.producto_codigo',
        'sequence:catalogo.producto_referencia_seq',('history:' + $CatalogReferenceTarget),
        'routine:catalogo.siguiente_referencia_producto()',
        'routine:catalogo.reservar_codigo_producto(text, uuid)',
        'routine:catalogo.proteger_codigos_producto()',
        'routine:catalogo.registrar_codigos_producto()')
    if ($Mode -eq 'Upgrade') {
        foreach ($key in $requiredNew) {
            [void]$allowedNew.Add($key)
        }
    }
    foreach ($key in $old.Keys) {
        $a = $old[$key]
        if ($Mode -eq 'Upgrade' -and $a.kind -in @('legacy','reference')) { continue }
        if (-not $new.ContainsKey($key)) { throw 'Faltan datos u objetos anteriores.' }
        $b = $new[$key]
        if ($Mode -eq 'Upgrade' -and $a.kind -eq 'product') {
            if ($a.hash -cne $b.hash -or ([long]$a.version + 1) -ne [long]$b.version) {
                throw 'Producto modificado fuera de referencia/version, o version incorrecta.'
            }
        } elseif ($Mode -eq 'Upgrade' -and $key -eq 'table:catalogo.producto') {
            if ([long]$a.count -ne [long]$b.count) { throw 'Cambio en cantidad de productos.' }
        } elseif ($Mode -eq 'Upgrade' -and $key -eq 'table:public.__EFMigrationsHistory') {
            if (([long]$a.count + 1) -ne [long]$b.count) { throw 'Cambio inesperado de historial.' }
        } elseif ($Mode -eq 'Upgrade' -and $key -eq 'shape:catalogo.producto') {
            if ($a.columnsHash -cne $b.columnsHash) { throw 'Columnas de producto alteradas.' }
        } elseif ($Mode -eq 'MigratedCopy' -and $key -eq 'table:catalogo.producto_codigo') {
            # The alias registration timestamps differ between rehearsal and real migration.
            if ([long]$a.count -ne [long]$b.count) { throw 'Cantidad de alias distinta al ensayo.' }
        } elseif (-not (Test-CatalogSnapshotRecordEqual -Before $a -After $b)) {
            throw 'Diferencia de datos, estructura o secuencia; detener y revisar.'
        }
    }
    if ($Mode -eq 'Upgrade') {
        $expectedAliases = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($row in $old.Values) {
            if ($row.kind -eq 'legacy') { [void]$expectedAliases.Add('alias:' + [string]$row.key) }
        }
        foreach ($row in $new.Values) {
            if ($row.kind -eq 'reference') { [void]$expectedAliases.Add('alias:' + [string]$row.key) }
        }
        foreach ($key in $expectedAliases) {
            if (-not $new.ContainsKey($key)) { throw 'Alias antiguo o referencia nueva sin conservar.' }
            [void]$allowedNew.Add($key)
        }
        if (-not $new.ContainsKey('table:catalogo.producto_codigo') -or
            [long]($new['table:catalogo.producto_codigo'].count) -ne $expectedAliases.Count) {
            throw 'Cantidad inesperada de alias.'
        }
        foreach ($key in $requiredNew) {
            if (-not $new.ContainsKey($key)) { throw 'Falta un objeto nuevo obligatorio.' }
        }
    }
    foreach ($key in $new.Keys) {
        if ($old.ContainsKey($key)) { continue }
        if ($Mode -eq 'Upgrade' -and ($new[$key].kind -in @('legacy','reference') -or $allowedNew.Contains($key))) { continue }
        throw 'Datos u objetos adicionales inesperados.'
    }
    Write-Host ('SNAPSHOT_' + $Mode.ToUpperInvariant() + '_OK')
}

function Invoke-CatalogControlledMode {
    param([ValidateSet('Preflight','Apply')][string]$Mode,[string]$Database,[pscredential]$Credential,[string]$ApiDll)
    Assert-CatalogDatabase $Database
    if ($Database -eq 'postgres' -or ($Database -eq 'controlplus' -and $Credential.UserName -eq 'controlplus_test_admin')) {
        throw 'El migrador no admite ese destino/rol.'
    }
    if (-not (Test-Path -LiteralPath $ApiDll -PathType Leaf)) { throw 'DLL API ausente.' }
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $child=@{}
    Push-Location (Split-Path -Parent $ApiDll)
    try {
        $builder['Host']='127.0.0.1'; $builder['Port']=5432; $builder['Database']=$Database
        $builder['Username']=$Credential.UserName; $builder['Password']=$Credential.GetNetworkCredential().Password
        $builder['Pooling']=$false
        if ($Mode -eq 'Preflight') { $builder['Options']='-c default_transaction_read_only=on' }
        $child['ConnectionStrings__ControlPlusDb']=$builder.psbase.ConnectionString
        $child['CONTROLPLUS_MIGRATIONS_ENABLED']='true'
        $child['DOTNET_ENVIRONMENT']='Development'; $child['ASPNETCORE_ENVIRONMENT']='Development'
        $flag = '--preflight-to'
        if ($Mode -eq 'Apply') { $flag='--migrate-to' }
        # Npgsql also reads PGOPTIONS. Remove it only from the child; Preflight
        # keeps its explicit read-only Options, Apply inherits no ambient options.
        $remove=@('PGOPTIONS') + @(Get-ChildItem Env: | Where-Object {
            $_.Name.Replace('__',':') -ieq 'ConnectionStrings:ControlPlusDb' -or
            $_.Name -imatch '^(CUSTOMCONNSTR_|SQLCONNSTR_|SQLAZURECONNSTR_|MYSQLCONNSTR_)ControlPlusDb$'
        } | ForEach-Object { $_.Name })
        $runtime=(Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
        $lines=@(Invoke-CatalogNativeProcess -Program $runtime -NativeArguments @($ApiDll,$flag,$CatalogReferenceTarget) `
            -EnvironmentOverrides $child -RemoveEnvironment $remove -Stage ('Controlled.'+$Mode) -Tool dotnet)
        if ($Mode -eq 'Preflight' -and -not ($lines -match 'Permanent references preflight:.*READ ONLY.*rollback=confirmed')) {
            throw 'No se obtuvo la confirmacion de lectura y rollback.'
        }
        return $lines
    } catch {
        if ($_.Exception.Data.Contains('CatalogDiagnostic')) { throw }
        throw 'Modo controlado no acreditado. Estado por verificar en solo lectura; no reintentar automaticamente.'
    } finally {
        $builder.Clear()
        $child.Clear()
        Pop-Location
    }
}
