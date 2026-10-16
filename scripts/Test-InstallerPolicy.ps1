param([string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$lines = [IO.File]::ReadAllLines((Join-Path $ProjectRoot 'installer\SarabDesktop.iss'))
foreach ($rule in @(@('DefaultDirName','{autopf}\SarabDesktop'), @('PrivilegesRequired','admin'), @('DisableDirPage','yes'))) {
    $entries = @($lines | Where-Object { $_ -match ('^\s*' + [regex]::Escape($rule[0]) + '\s*=') })
    if ($entries.Count -ne 1 -or $entries[0].Trim() -cne ($rule[0] + '=' + $rule[1])) { throw ('Required protected installer policy changed: ' + $rule[0]) }
}
Write-Output 'Protected installer policy verified.'
