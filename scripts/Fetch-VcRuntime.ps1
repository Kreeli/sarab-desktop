param([string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent), [Parameter(Mandatory = $true)][string]$VisualStudio)
$ErrorActionPreference = 'Stop'
function Assert-MicrosoftSignature([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') { throw ('Invalid Microsoft signature on runtime dependency: ' + $Path) }
}
$pin = (Get-Content -LiteralPath (Join-Path $ProjectRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json).vcRuntime
$cacheRoot = Join-Path $ProjectRoot ('vendor\vcredist-' + $pin.version)
New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
$redistributable = Join-Path $cacheRoot 'VC_redist.x64.exe'
if (-not (Test-Path -LiteralPath $redistributable -PathType Leaf)) {
    $download = Join-Path $cacheRoot ('download-' + [guid]::NewGuid().ToString('N') + '.exe')
    Invoke-WebRequest -UseBasicParsing -Uri $pin.url -OutFile $download
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $pin.sha256) { throw 'Microsoft VC runtime download SHA256 mismatch. The pinned dependency was not accepted.' }
    Assert-MicrosoftSignature $download
    if ((Get-Item -LiteralPath $download).VersionInfo.FileVersion -ne $pin.version) { throw 'Microsoft VC runtime version mismatch.' }
    Move-Item -LiteralPath $download -Destination $redistributable
}
if ((Get-FileHash -LiteralPath $redistributable -Algorithm SHA256).Hash -ne $pin.sha256) { throw 'Cached Microsoft VC runtime SHA256 mismatch.' }
Assert-MicrosoftSignature $redistributable
if ((Get-Item -LiteralPath $redistributable).VersionInfo.FileVersion -ne $pin.version) { throw 'Cached Microsoft VC runtime version mismatch.' }
$redistRoot = Join-Path $VisualStudio 'VC\Redist\MSVC'
$versions = @(Get-ChildItem -LiteralPath $redistRoot -Directory | Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Name } -Descending)
$runtimeDll = $null
foreach ($version in $versions) {
    $runtimeDll = Get-ChildItem -Path (Join-Path $version.FullName 'x64\Microsoft.VC*.CRT\vcruntime140.dll') -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($runtimeDll) { break }
}
if (-not $runtimeDll) { throw 'The Visual Studio x64 redistributable vcruntime140.dll was not found. Install the C++ redistributable build component.' }
Assert-MicrosoftSignature $runtimeDll.FullName
return @{ Redistributable = $redistributable; AppLocalRuntime = $runtimeDll.FullName }
