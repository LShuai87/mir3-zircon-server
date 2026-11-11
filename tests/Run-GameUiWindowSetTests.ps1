# Exercises real Client controls in an isolated process; no network or game login.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PackagePath, [string]$CompilerPath, [string]$EvidenceDirectory, [string]$AlphaReferencePath, [string]$ClientDirectory, [switch]$ChatOnly)
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
if (!$ClientDirectory) { $ClientDirectory = Join-Path $repositoryRoot '..\Debug\Client' }
$clientDirectory = [IO.Path]::GetFullPath($ClientDirectory)
$client = Join-Path $clientDirectory 'Zircon.exe'
if (!(Test-Path -LiteralPath $client)) { throw 'Build Client Debug first.' }
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ZirconWindowSetTests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    $contract = Get-Content -LiteralPath (Join-Path $repositoryRoot 'build/game-ui/layout-official-v1.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $placements = foreach ($resolution in $contract.resolutions) {
        foreach ($owner in $resolution.placements.PSObject.Properties) {
            [string]$resolution.width + 'x' + $resolution.height + '|' + $owner.Name + '|' + ($owner.Value -join ',')
        }
    }
    $fixture = Join-Path $testDirectory 'expected-placements.txt'
    [IO.File]::WriteAllLines($fixture, [string[]]$placements, [Text.Encoding]::UTF8)
    Copy-Item -LiteralPath $client -Destination $testDirectory
    Get-ChildItem -LiteralPath $clientDirectory -Filter '*.dll' | Copy-Item -Destination $testDirectory
    $executable = Join-Path $testDirectory 'OfficialUiWindowSetTests.exe'
    & $CompilerPath /nologo /target:exe /platform:x86 /r:System.Drawing.dll /r:System.Windows.Forms.dll ('/r:' + $client) ('/r:' + (Join-Path $clientDirectory 'Library.dll')) ('/r:' + (Join-Path $clientDirectory 'SlimDX.dll')) ('/out:' + $executable) (Join-Path $PSScriptRoot 'game-ui\OfficialUiWindowSetTests.cs') (Join-Path $PSScriptRoot 'game-ui\OfficialUiSceneStartupTests.cs') (Join-Path $PSScriptRoot 'game-ui\OfficialUiButtonHitMaskTests.cs') (Join-Path $PSScriptRoot 'game-ui\OfficialUiChatPersistenceTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'WindowSet test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'ImageManager\app.config') -Destination ($executable + '.config')
    Push-Location -LiteralPath $testDirectory
    try {
        if ($ChatOnly) { & $executable $PackagePath $testDirectory $fixture '--chat-only' }
        elseif ($AlphaReferencePath) { & $executable $PackagePath $testDirectory $fixture $AlphaReferencePath }
        else { & $executable $PackagePath $testDirectory $fixture }
        if ($LASTEXITCODE -ne 0) { throw 'WindowSet tests failed.' }
        if ($EvidenceDirectory) {
            [IO.Directory]::CreateDirectory($EvidenceDirectory) | Out-Null
            $runtime = foreach ($name in @('Zircon.exe', 'Library.dll', 'SlimDX.dll')) {
                $file = Join-Path $testDirectory $name
                [ordered]@{ name = $name; sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() }
            }
            $runtime | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'tested-runtime.json') -Encoding UTF8
            $evidenceFiles = @('command-actions.csv', 'hud-orb-empty.png', 'hud-orb-half.png', 'hud-orb-full.png', 'legacy-glyph-suppressed.png', 'legacy-glyph-restored.png', 'button-hitmask-cpu.txt', 'layout-persistence-results.txt', 'layout-persistence-saved.json', 'layout-persistence-legacy.db', 'chat-reconstruction-results.txt', 'chat-persistence-results.txt', 'chat-persistence-saved.json', 'chat-persistence-legacy.db')
            if ($ChatOnly) { $evidenceFiles = @('chat-reconstruction-results.txt', 'chat-persistence-results.txt', 'chat-persistence-saved.json', 'chat-persistence-legacy.db', 'chat-receiving.png', 'chat-rejected.png') }
            foreach ($name in $evidenceFiles) {
                Copy-Item -LiteralPath (Join-Path $testDirectory $name) -Destination (Join-Path $EvidenceDirectory $name)
            }
        }
    }
    finally { Pop-Location }
} finally {
    $resolved = [IO.Path]::GetFullPath($testDirectory)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ZirconWindowSetTests-[a-f0-9]{32}$') { throw 'Refusing cleanup outside generated package preflight test directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
