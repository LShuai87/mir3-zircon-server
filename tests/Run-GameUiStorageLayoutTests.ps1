# Exercises real Client controls in an isolated process; no network or game login.
[CmdletBinding()]
param([string]$CompilerPath, [string]$LegacyDataDirectory)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (!$CompilerPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $CompilerPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw 'C# compiler not found.' }
$clientDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '..\Debug\Client'))
$client = Join-Path $clientDirectory 'Zircon.exe'
if (!(Test-Path -LiteralPath $client)) { throw 'Build Client Debug first.' }
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ZirconStorageLayoutTests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    if (!$LegacyDataDirectory) { $LegacyDataDirectory = Join-Path $clientDirectory 'Data' }
    $LegacyDataDirectory = (Resolve-Path -LiteralPath $LegacyDataDirectory).Path
    $layoutInput = Get-Content -LiteralPath (Join-Path $repositoryRoot 'build/game-ui/warehouse-layout-candidate.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $expected = @($layoutInput.filter -join ','; $layoutInput.grid -join ','; $layoutInput.scrollbar -join ',')
    $fixture = Join-Path $testDirectory 'expected-layout.txt'
    [IO.File]::WriteAllLines($fixture,$expected,[Text.Encoding]::UTF8)
    Copy-Item -LiteralPath $client -Destination $testDirectory
    Get-ChildItem -LiteralPath $clientDirectory -Filter '*.dll' | Copy-Item -Destination $testDirectory
    $executable = Join-Path $testDirectory 'OfficialUiStorageLayoutTests.exe'
    & $CompilerPath /nologo /target:exe /platform:x86 /r:System.Drawing.dll /r:System.Windows.Forms.dll ('/r:' + $client) ('/r:' + (Join-Path $clientDirectory 'Library.dll')) ('/r:' + (Join-Path $clientDirectory 'SlimDX.dll')) ('/out:' + $executable) (Join-Path $PSScriptRoot 'game-ui\OfficialUiStorageLayoutTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Storage layout test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'ImageManager\app.config') -Destination ($executable + '.config')
    Push-Location -LiteralPath $testDirectory
    try { & $executable $fixture $LegacyDataDirectory; if ($LASTEXITCODE -ne 0) { throw 'Storage layout tests failed.' } }
    finally { Pop-Location }
} finally {
    $resolved = [IO.Path]::GetFullPath($testDirectory)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ZirconStorageLayoutTests-[a-f0-9]{32}$') { throw 'Refusing cleanup outside generated storage layout test directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
