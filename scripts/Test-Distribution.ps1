param([Parameter(Mandatory = $true)][string]$DistributionRoot)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($DistributionRoot).TrimEnd('\')
$required = @('SarabDesktop.exe','SarabDesktop.Helper.exe','backend\sarab.exe','backend\location-probe.exe','driver\SarabGnss.dll','driver\SarabGnss.cat','driver\SarabGnss.inf','driver\SarabGnss.cer','Start.cmd','scripts\Start-SarabDesktop.ps1','scripts\Test-Distribution.ps1')
$required += @('backend\vcruntime140.dll','runtime\VC_redist.x64.exe')
$manifest = Join-Path $root 'SHA256SUMS.txt'
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { throw 'Package checksum manifest is missing. Extract the complete release archive again.' }
$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($line in [IO.File]::ReadAllLines($manifest)) {
    if ($line -notmatch '^([0-9a-fA-F]{64})  (.+)$') { throw 'Invalid package checksum manifest.' }
    $expected = $matches[1]
    $relative = $matches[2]
    $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if ([IO.Path]::IsPathRooted($relative) -or -not $path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package checksum path.' }
    $canonicalRelative = $path.Substring($root.Length + 1)
    if (-not $seen.Add($canonicalRelative)) { throw 'Duplicate package checksum path.' }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Package file is missing: ' + $relative) }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) { throw ('Package file is damaged or changed: ' + $relative + '. Extract the complete release archive again.') }
}
foreach ($relative in $required) { if (-not $seen.Contains($relative)) { throw ('Required package file is not listed in the manifest: ' + $relative) } }
Write-Output ('Verified package: ' + $root)
