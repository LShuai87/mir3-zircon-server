# Build the Debug client first; run production HUD logic and real control integration in an isolated process.
[CmdletBinding()]
param([string]$CompilerPath, [string]$RenderPath)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (!$CompilerPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (!(Test-Path -LiteralPath $vswhere)) { throw 'VS compiler discovery requires vswhere.exe; pass -CompilerPath explicitly.' }
    $CompilerPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw 'VS Roslyn csc.exe was not found; pass -CompilerPath explicitly.' }
$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('ZirconMonsterHudTests-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($testDirectory) | Out-Null
$executable = Join-Path $testDirectory 'MonsterHudTests.exe'
$clientDirectory = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot '..\Debug\Client'))
$client = Join-Path $clientDirectory 'Zircon.exe'
if (!(Test-Path -LiteralPath $client)) { throw 'Build Client Debug before running Monster HUD tests.' }
Copy-Item -LiteralPath $client -Destination $testDirectory
Get-ChildItem -LiteralPath $clientDirectory -Filter '*.dll' | Copy-Item -Destination $testDirectory
& $CompilerPath /nologo /target:exe /platform:x86 /r:System.Drawing.dll /r:System.Windows.Forms.dll ('/r:' + $client) ('/r:' + (Join-Path $clientDirectory 'Library.dll')) ('/r:' + (Join-Path $clientDirectory 'SlimDX.dll')) ('/out:' + $executable) (Join-Path $repositoryRoot 'Client\Models\MonsterHudState.cs') (Join-Path $PSScriptRoot 'MonsterHudTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Monster HUD regression compilation failed.' }
if ($RenderPath) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'data\Data\GameInter.Zl') -Destination $testDirectory
}
Push-Location -LiteralPath $testDirectory
try {
    if ($RenderPath) { & $executable $RenderPath } else { & $executable }
    if ($LASTEXITCODE -ne 0) { throw 'Monster HUD regressions failed.' }
} finally { Pop-Location }
