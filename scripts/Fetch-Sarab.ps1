param([string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$pins = Get-Content -LiteralPath (Join-Path $ProjectRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
$vendor = Join-Path $ProjectRoot ('vendor\sarab-' + $pins.sarabVersion)
$archive = Join-Path $ProjectRoot ('vendor\sarab-' + $pins.sarabVersion + '-x64.zip')
$complete = Test-Path -LiteralPath $vendor
foreach ($file in $pins.files.PSObject.Properties) { $path = Join-Path $vendor $file.Name; if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.Value) { $complete = $false } }
if (-not $complete) {
    New-Item -ItemType Directory -Path (Split-Path $archive -Parent) -Force | Out-Null
    if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -UseBasicParsing -Uri $pins.sarabUrl -OutFile $archive }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $pins.archiveSha256) { throw 'Upstream archive SHA256 mismatch. Do not install this package.' }
    $extracted = Join-Path $ProjectRoot ('build\upstream-' + [guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath $archive -DestinationPath $extracted
    New-Item -ItemType Directory -Path $vendor -Force | Out-Null
    foreach ($file in $pins.files.PSObject.Properties) {
        $matches = @(Get-ChildItem -LiteralPath $extracted -File -Recurse | Where-Object Name -eq $file.Name)
        if ($matches.Count -ne 1 -or (Get-FileHash -LiteralPath $matches[0].FullName -Algorithm SHA256).Hash -ne $file.Value) { throw ('Pinned upstream file mismatch: ' + $file.Name) }
        Copy-Item -LiteralPath $matches[0].FullName -Destination (Join-Path $vendor $file.Name) -Force
    }
}
Write-Output $vendor
