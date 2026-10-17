param([string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
# Run in Windows PowerShell 5.1, matching the application's .NET Framework runtime.
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $ProjectRoot 'dist\SarabDesktop.exe'))
$validate = $assembly.GetType('SarabDesktop.Installation').GetMethod('ValidateStateEntry', [Reflection.BindingFlags]'Static,NonPublic')
if (-not $validate) { throw 'State-path validator is missing.' }
$fixture = Join-Path $ProjectRoot ('build\state-path-test-' + [guid]::NewGuid().ToString('N'))
$target = Join-Path $fixture 'target'
New-Item -ItemType Directory -Path $target -Force | Out-Null
$junction = Join-Path $fixture 'junction'
New-Item -ItemType Junction -Path $junction -Target $target | Out-Null
$file = Join-Path $target 'state.dat'
[IO.File]::WriteAllText($file, 'isolated-state-path-test')
$hardlink = Join-Path $target 'hardlink.dat'
New-Item -ItemType HardLink -Path $hardlink -Target $file | Out-Null
foreach ($case in @(@{ Path = $junction; Directory = $true; Name = 'junction' }, @{ Path = $hardlink; Directory = $false; Name = 'hardlink' })) {
    $rejected = $false
    try {
        $handle = $validate.Invoke($null, [object[]]@([string]$case.Path, [bool]$case.Directory))
        if ($handle) { $handle.Dispose() }
    } catch {
        $statePathException = $_.Exception
        while ($statePathException.InnerException) { $statePathException = $statePathException.InnerException }
        if ($statePathException -isnot [InvalidOperationException]) { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw ('Unsafe state path accepted: ' + $case.Name) }
    Write-Output ('Passed native state-path rejection: ' + $case.Name)
}
# Fixtures remain under build/. No ProgramData, driver, certificate or boot state is changed.
