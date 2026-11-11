<#
.SYNOPSIS
  Carga productos de EJEMPLO de una tienda de variedades mediante la API.

.DESCRIPTION
  Son datos de prueba (no reales) para probar Ventas. El script NO se conecta a la base de datos:
  usa los endpoints /api/categories y /api/products con el login de un Administrador, así que escribe
  en la base a la que esté conectada la API que indiques en -ApiBase.

  Úsalo contra una API conectada a una base de PRUEBAS. Si lo apuntas a la base real `controlplus`,
  haz antes un respaldo y recuerda que los productos no se borran (solo se inactivan).

  Es repetible: si una categoría o un producto con el mismo nombre ya existe, lo omite.

.EXAMPLE
  # Desde C:\ControlPlus\backend\ControlPlus.Backend
  .\scripts\Seed-DemoProducts.ps1 -ApiBase http://localhost:5000 -UserName admin
#>
[CmdletBinding()]
param(
  [string]$ApiBase = 'http://localhost:5000',
  [Parameter(Mandatory = $true)][string]$UserName
)

$ErrorActionPreference = 'Stop'
$ApiBase = $ApiBase.TrimEnd('/')

# Categoría -> abreviatura (2 a 10 letras ASCII, única). Define el prefijo de la referencia (p. ej. PAP-000004).
$categories = @(
  @{ Name = 'Papelería';  Abbr = 'PAP' },
  @{ Name = 'Juguetes';   Abbr = 'JUG' },
  @{ Name = 'Bisutería';  Abbr = 'BIS' },
  @{ Name = 'Ferretería'; Abbr = 'FER' },
  @{ Name = 'Hogar';      Abbr = 'HOG' },
  @{ Name = 'Belleza';    Abbr = 'BEL' }
)

# Precios y costos en COP enteros. Siempre: costo < mayorista < minorista, para que se pueda vender.
# Unit: 'unidad' | 'paquete' | 'metro'. Stock y costo inicial se registran como INVENTARIO_INICIAL.
$products = @(
  @{ Cat = 'Papelería';  Name = 'Cuaderno cuadriculado 100 hojas'; Unit = 'unidad';  Retail = 4500;  Wholesale = 4000;  Cost = 2800;  Min = 10; Stock = 60 },
  @{ Cat = 'Papelería';  Name = 'Lápiz grafito HB';                 Unit = 'unidad';  Retail = 800;   Wholesale = 650;   Cost = 400;   Min = 30; Stock = 200 },
  @{ Cat = 'Papelería';  Name = 'Resma papel carta 500 hojas';      Unit = 'paquete'; Retail = 17500; Wholesale = 16000; Cost = 13500; Min = 5;  Stock = 25 },
  @{ Cat = 'Papelería';  Name = 'Pegante en barra';                 Unit = 'unidad';  Retail = 2500;  Wholesale = 2200;  Cost = 1500;  Min = 10; Stock = 40 },
  @{ Cat = 'Juguetes';   Name = 'Carro de fricción';                Unit = 'unidad';  Retail = 12000; Wholesale = 10500; Cost = 7000;  Min = 5;  Stock = 24 },
  @{ Cat = 'Juguetes';   Name = 'Muñeca articulada';                Unit = 'unidad';  Retail = 19000; Wholesale = 17000; Cost = 12000; Min = 5;  Stock = 18 },
  @{ Cat = 'Juguetes';   Name = 'Rompecabezas 100 piezas';          Unit = 'unidad';  Retail = 9500;  Wholesale = 8500;  Cost = 5500;  Min = 5;  Stock = 20 },
  @{ Cat = 'Bisutería';  Name = 'Manilla ajustable';                Unit = 'unidad';  Retail = 6000;  Wholesale = 5000;  Cost = 2500;  Min = 10; Stock = 50 },
  @{ Cat = 'Bisutería';  Name = 'Collar con dije';                  Unit = 'unidad';  Retail = 11000; Wholesale = 9500;  Cost = 5500;  Min = 8;  Stock = 30 },
  @{ Cat = 'Bisutería';  Name = 'Aretes pequeños (par)';            Unit = 'unidad';  Retail = 5000;  Wholesale = 4200;  Cost = 2000;  Min = 10; Stock = 45 },
  @{ Cat = 'Ferretería'; Name = 'Cinta aislante negra';             Unit = 'unidad';  Retail = 3500;  Wholesale = 3000;  Cost = 1900;  Min = 10; Stock = 35 },
  @{ Cat = 'Ferretería'; Name = 'Cable dúplex calibre 16';          Unit = 'metro';   Retail = 2200;  Wholesale = 1900;  Cost = 1300;  Min = 20; Stock = 150 },
  @{ Cat = 'Ferretería'; Name = 'Destornillador de pala';           Unit = 'unidad';  Retail = 7500;  Wholesale = 6500;  Cost = 4200;  Min = 5;  Stock = 22 },
  @{ Cat = 'Hogar';      Name = 'Vaso plástico 12 oz';              Unit = 'paquete'; Retail = 8000;  Wholesale = 7000;  Cost = 4800;  Min = 8;  Stock = 30 },
  @{ Cat = 'Hogar';      Name = 'Escoba';                           Unit = 'unidad';  Retail = 12500; Wholesale = 11000; Cost = 8000;  Min = 5;  Stock = 15 },
  @{ Cat = 'Hogar';      Name = 'Recipiente con tapa 1 L';          Unit = 'unidad';  Retail = 9000;  Wholesale = 8000;  Cost = 5200;  Min = 6;  Stock = 28 },
  @{ Cat = 'Belleza';    Name = 'Peinilla de bolsillo';             Unit = 'unidad';  Retail = 2000;  Wholesale = 1700;  Cost = 900;   Min = 15; Stock = 60 },
  @{ Cat = 'Belleza';    Name = 'Esmalte de uñas';                  Unit = 'unidad';  Retail = 4500;  Wholesale = 3800;  Cost = 2200;  Min = 10; Stock = 40 }
)

function Invoke-Api {
  param([string]$Method, [string]$Path, $Body = $null, [hashtable]$Headers = @{})
  $uri = "$ApiBase$Path"
  $params = @{ Method = $Method; Uri = $uri; Headers = $Headers }
  if ($null -ne $Body) {
    $json = $Body | ConvertTo-Json -Depth 6 -Compress
    $params.Body = [System.Text.Encoding]::UTF8.GetBytes($json)   # conserva tildes y ñ
    $params.ContentType = 'application/json; charset=utf-8'
  }
  try { return Invoke-RestMethod @params }
  catch {
    $detail = if ($_.ErrorDetails.Message) { $_.ErrorDetails.Message } else { $_.Exception.Message }
    throw "Falló $Method $Path -> $detail"
  }
}

Write-Host ''
Write-Host "API destino: $ApiBase" -ForegroundColor Yellow
Write-Host 'Este script ESCRIBE categorías, productos y existencias iniciales en la base conectada a esa API.' -ForegroundColor Yellow
Write-Host "Se cargarán $($categories.Count) categorías y $($products.Count) productos de ejemplo." -ForegroundColor Yellow
if ((Read-Host 'Escribe CARGAR para continuar') -ne 'CARGAR') { Write-Host 'Cancelado. No se escribió nada.'; return }

$secure = Read-Host "Contraseña de $UserName" -AsSecureString
$plain = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR([System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
$login = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{ userName = $UserName; password = $plain }
$plain = $null
$auth = @{ Authorization = "Bearer $($login.accessToken)" }

# Unidades de medida (Unidad, Paquete, Metro).
$units = Invoke-Api -Method GET -Path '/api/measurement-units' -Headers $auth
function Find-Unit([string]$kind) {
  $match = $units | Where-Object { $_.isActive -and ("$($_.code) $($_.name)" -match $kind) } | Select-Object -First 1
  if (-not $match) { throw "No encontré la unidad '$kind'. Unidades disponibles: $(($units | ForEach-Object { $_.code }) -join ', ')" }
  return $match.id
}
$unitIds = @{ unidad = Find-Unit 'unidad|und'; paquete = Find-Unit 'paquete|paq'; metro = Find-Unit 'metro|mt' }

# Categorías (omite las que ya existen por nombre).
$categoryIds = @{}
foreach ($category in $categories) {
  $found = Invoke-Api -Method GET -Path "/api/categories?search=$([uri]::EscapeDataString($category.Name))&includeInactive=true&page=1&pageSize=20" -Headers $auth
  $existing = $found.items | Where-Object { $_.name -eq $category.Name } | Select-Object -First 1
  if ($existing) { $categoryIds[$category.Name] = $existing.id; Write-Host "= Categoría existente: $($category.Name)"; continue }
  $created = Invoke-Api -Method POST -Path '/api/categories' -Headers $auth -Body @{ name = $category.Name; description = $null; abbreviation = $category.Abbr }
  $categoryIds[$category.Name] = $created.id
  Write-Host "+ Categoría creada: $($category.Name) ($($category.Abbr))" -ForegroundColor Green
}

# Productos (omite los que ya existen por nombre).
$created = 0; $skipped = 0
foreach ($product in $products) {
  $found = Invoke-Api -Method GET -Path "/api/products?search=$([uri]::EscapeDataString($product.Name))&includeInactive=true&page=1&pageSize=20" -Headers $auth
  if ($found.items | Where-Object { $_.name -eq $product.Name }) { $skipped++; Write-Host "= Producto existente: $($product.Name)"; continue }
  $body = @{
    categoryId = $categoryIds[$product.Cat]; measurementUnitId = $unitIds[$product.Unit]; supplierId = $null
    internalCode = $null; barcode = $null; barcodeFormat = $null; generateBarcode = $true
    name = $product.Name; description = $null; retailPrice = $product.Retail; wholesalePrice = $product.Wholesale
    taxPercentage = $null; minimumStock = $product.Min; initialQuantity = $product.Stock; initialUnitCost = $product.Cost
  }
  $result = Invoke-Api -Method POST -Path '/api/products' -Headers ($auth + @{ 'Idempotency-Key' = [guid]::NewGuid().ToString() }) -Body $body
  $created++
  Write-Host ("+ {0}  {1}  `${2:N0}" -f $result.internalCode, $product.Name, $product.Retail) -ForegroundColor Green
}

Write-Host ''
Write-Host "Listo. Productos creados: $created. Omitidos por existir: $skipped." -ForegroundColor Cyan
