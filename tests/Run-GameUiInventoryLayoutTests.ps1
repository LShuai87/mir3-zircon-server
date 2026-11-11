# Exercises real Client controls in an isolated process; no network or game login.
[CmdletBinding()]
param([string]$CompilerPath, [string]$PackagePath, [string]$EvidenceDirectory, [string]$ClientDirectory)
if ([bool]$PackagePath -ne [bool]$EvidenceDirectory) { throw 'Provide both PackagePath and EvidenceDirectory for render probes.' }
if ($PackagePath) { $PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path; $EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory); if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Use a fresh evidence directory.' } }
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (!$CompilerPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $CompilerPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw 'C# compiler not found.' }
if (!$ClientDirectory) { $ClientDirectory = Join-Path $repositoryRoot '..\Debug\Client' }
$clientDirectory = [IO.Path]::GetFullPath($ClientDirectory)
$client = Join-Path $clientDirectory 'Zircon.exe'
if (!(Test-Path -LiteralPath $client)) { throw 'Build Client Debug first.' }
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ZirconInventoryLayoutTests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    $layoutInput = Get-Content -LiteralPath (Join-Path $repositoryRoot 'build/game-ui/layout-official-v1.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $byId = @{}
    foreach ($node in $layoutInput.nodes) { $byId[$node.id] = $node }
    $first = $byId['inventory.cell.0'].rect
    $last = $byId['inventory.cell.48'].rect
    $expected = @(($first[0],$first[1],($last[0]+$last[2]-$first[0]),($last[1]+$last[3]-$first[1]) -join ','))
    foreach ($id in @('inventory.caption.weight','inventory.weight','inventory.caption.gold','inventory.gold','inventory.action.sell','inventory.action.fragment','inventory.action.drops')) {
        $expected += ($byId[$id].rect -join ',')
    }
    for ($i=0; $i -lt 49; $i++) { $expected += ($byId[('inventory.cell.' + $i)].rect -join ',') }
    $fixture = Join-Path $testDirectory 'expected-layout.txt'
    [IO.File]::WriteAllLines($fixture,$expected,[Text.Encoding]::UTF8)
    Copy-Item -LiteralPath $client -Destination $testDirectory
    Get-ChildItem -LiteralPath $clientDirectory -Filter '*.dll' | Copy-Item -Destination $testDirectory
    $executable = Join-Path $testDirectory 'OfficialUiInventoryLayoutTests.exe'
    & $CompilerPath /nologo /target:exe /platform:x86 /r:System.Drawing.dll /r:System.Windows.Forms.dll ('/r:' + $client) ('/r:' + (Join-Path $clientDirectory 'Library.dll')) ('/r:' + (Join-Path $clientDirectory 'SlimDX.dll')) ('/out:' + $executable) (Join-Path $PSScriptRoot 'game-ui\OfficialUiInventoryLayoutTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Inventory layout test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'ImageManager\app.config') -Destination ($executable + '.config')
    Push-Location -LiteralPath $testDirectory
    try { if ($PackagePath) { & $executable $fixture $PackagePath $EvidenceDirectory } else { & $executable $fixture }; if ($LASTEXITCODE -ne 0) { throw 'Inventory layout tests failed.' } }
    finally { Pop-Location }
} finally {
    $resolved = [IO.Path]::GetFullPath($testDirectory)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ZirconInventoryLayoutTests-[a-f0-9]{32}$') { throw 'Refusing cleanup outside generated inventory layout test directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
