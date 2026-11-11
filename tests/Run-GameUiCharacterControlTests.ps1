# Exercises real Client controls in an isolated process; no network or game login.
[CmdletBinding()]
param([string]$CompilerPath)
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
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ZirconCharacterControlTests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    $layoutInput = Get-Content -LiteralPath (Join-Path $repositoryRoot 'build/game-ui/layout-official-v1.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $expected = @()
    foreach ($node in $layoutInput.nodes | Where-Object { $_.owner -eq 'character' -and ($_.id -match '^character\.(control\.|action\.hermit\.|equipment\..+\.caption$|caption\.new\.|caption\.existing\.)' -or $_.id -eq 'character.hermit.confirmation') }) {
        $id = $node.id -replace '^character\.',''
        $pageName = if ($node.id -match '^character\.control\.(CharacterNameLabel|MarriageIcon)$') { 'Chrome' } else { @{character='Character';extraEquipment='ExtraEquipment';hermit='Hermit'}[$node.page] }
        $text = if ($null -eq $node.label) { '-' } else { $node.label }
        $expected += ($id + '=' + ($pageName,($node.rect -join ','),$text -join '|'))
    }
    $fixture = Join-Path $testDirectory 'expected-layout.txt'
    [IO.File]::WriteAllLines($fixture,$expected,[Text.Encoding]::UTF8)
    Copy-Item -LiteralPath $client -Destination $testDirectory
    Get-ChildItem -LiteralPath $clientDirectory -Filter '*.dll' | Copy-Item -Destination $testDirectory
    $executable = Join-Path $testDirectory 'OfficialUiCharacterControlTests.exe'
    & $CompilerPath /nologo /target:exe /platform:x86 /r:System.Drawing.dll /r:System.Windows.Forms.dll ('/r:' + $client) ('/r:' + (Join-Path $clientDirectory 'Library.dll')) ('/r:' + (Join-Path $clientDirectory 'SlimDX.dll')) ('/out:' + $executable) (Join-Path $PSScriptRoot 'game-ui\OfficialUiCharacterControlTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Character control test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'ImageManager\app.config') -Destination ($executable + '.config')
    Push-Location -LiteralPath $testDirectory
    try { & $executable $fixture; if ($LASTEXITCODE -ne 0) { throw 'Character control tests failed.' } }
    finally { Pop-Location }
} finally {
    $resolved = [IO.Path]::GetFullPath($testDirectory)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ZirconCharacterControlTests-[a-f0-9]{32}$') { throw 'Refusing cleanup outside generated character control test directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
