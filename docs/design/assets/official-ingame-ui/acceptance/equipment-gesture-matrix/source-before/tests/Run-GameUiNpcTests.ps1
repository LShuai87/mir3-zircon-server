# Exercises normal GameScene constructors and real D3D reset using isolated synthetic data; no network or login.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PackagePath, [string]$CompilerPath, [Parameter(Mandatory = $true)][string]$EvidenceDirectory, [string]$LegacyDataDirectory)
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$ErrorActionPreference = 'Stop'
if ($EvidenceDirectory) {
    $EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
    if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Use a fresh evidence directory.' }
}
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (!$CompilerPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $CompilerPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw 'C# compiler not found.' }
$clientDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '..\Debug\Client'))
$client = Join-Path $clientDirectory 'Zircon.exe'
if (!(Test-Path -LiteralPath $client)) { throw 'Build Client Debug first.' }
if (!$LegacyDataDirectory) { $LegacyDataDirectory = Join-Path $clientDirectory 'Data' }
$LegacyDataDirectory = (Resolve-Path -LiteralPath $LegacyDataDirectory).Path
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ZirconNpcTests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    Copy-Item -LiteralPath $client -Destination $testDirectory
    Get-ChildItem -LiteralPath $clientDirectory -Filter '*.dll' | Copy-Item -Destination $testDirectory
    $executable = Join-Path $testDirectory 'OfficialUiNpcTests.exe'
    & $CompilerPath /nologo /target:exe /platform:x86 /r:System.Drawing.dll /r:System.Windows.Forms.dll ('/r:' + $client) ('/r:' + (Join-Path $clientDirectory 'Library.dll')) ('/r:' + (Join-Path $clientDirectory 'SlimDX.dll')) ('/out:' + $executable) /main:OfficialUiNpcEntry (Join-Path $PSScriptRoot 'game-ui\OfficialUiFullSceneTests.cs') (Join-Path $PSScriptRoot 'game-ui\OfficialUiNpcTests.cs') (Join-Path $PSScriptRoot 'game-ui\OfficialUiNpcGoodsTests.cs') (Join-Path $PSScriptRoot 'game-ui\OfficialUiNpcItemServiceTests.cs') (Join-Path $PSScriptRoot 'game-ui\OfficialUiNpcRefineTests.cs') (Join-Path $PSScriptRoot 'game-ui\OfficialUiNpcQuestTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Npc test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'ImageManager\app.config') -Destination ($executable + '.config')
    Push-Location -LiteralPath $testDirectory
    try {
        & $executable $PackagePath $LegacyDataDirectory $EvidenceDirectory
        if ($LASTEXITCODE -ne 0) { throw 'Npc tests failed.' }
        $runtime = foreach ($name in @('Zircon.exe', 'Library.dll', 'SlimDX.dll')) {
            $file = Join-Path $testDirectory $name
            [ordered]@{ name = $name; sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
        $runtime | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'tested-runtime.json') -Encoding UTF8

    }
    finally { Pop-Location }
} finally {
    $resolved = [IO.Path]::GetFullPath($testDirectory)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ZirconNpcTests-[a-f0-9]{32}$') { throw 'Refusing cleanup outside generated NPC test directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
