param([string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$verify = Join-Path $ProjectRoot 'scripts\Test-Distribution.ps1'
$fixture = Join-Path $ProjectRoot ('build\deployment-test-' + [guid]::NewGuid().ToString('N'))
$files = @('SarabDesktop.exe','SarabDesktop.Helper.exe','backend\sarab.exe','backend\location-probe.exe','driver\SarabGnss.dll','driver\SarabGnss.cat','driver\SarabGnss.inf','driver\SarabGnss.cer','Start.cmd','scripts\Start-SarabDesktop.ps1','scripts\Test-Distribution.ps1')
$files += @('backend\vcruntime140.dll','runtime\VC_redist.x64.exe')
foreach ($relative in $files) {
    $path = Join-Path $fixture $relative
    New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($path, 'integrity-test-fixture')
}
$manifest = Join-Path $fixture 'SHA256SUMS.txt'
$original = @($files | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $fixture $_) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_ })
[IO.File]::WriteAllLines($manifest, $original)
& $verify -DistributionRoot $fixture | Out-Null
function Assert-Rejected([string]$Case) {
    $rejected = $false
    try { & $verify -DistributionRoot $fixture | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw ('Integrity test did not reject: ' + $Case) }
    Write-Output ('Passed: ' + $Case)
}
[IO.File]::AppendAllText((Join-Path $fixture 'SarabDesktop.exe'), 'corruption')
Assert-Rejected 'modified executable'
[IO.File]::WriteAllText((Join-Path $fixture 'SarabDesktop.exe'), 'integrity-test-fixture')
[IO.File]::WriteAllLines($manifest, $original[1..($original.Length - 1)])
Assert-Rejected 'required executable omitted from manifest'
[IO.File]::WriteAllLines($manifest, ($original + ($original[0].Substring(0, 66) + '..\outside.exe')))
Assert-Rejected 'parent-directory manifest path'
[IO.File]::WriteAllLines($manifest, ($original + $original[0]))
Assert-Rejected 'duplicate manifest path'
[IO.File]::WriteAllLines($manifest, ($original + ($original[0].Substring(0, 66) + '.\SarabDesktop.exe')))
Assert-Rejected 'canonical duplicate manifest path'
[IO.File]::WriteAllText($manifest, 'invalid')
Assert-Rejected 'malformed manifest'
[IO.File]::WriteAllLines($manifest, $original)
Move-Item -LiteralPath (Join-Path $fixture 'driver\SarabGnss.cat') -Destination (Join-Path $fixture 'driver\SarabGnss.cat.missing')
Assert-Rejected 'missing driver catalog'
Write-Output 'Deployment integrity tests passed (valid package and seven rejection cases). Fixtures remain under build/.'
