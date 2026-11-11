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
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ZirconCharacterAttributeTests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    $layoutInput = Get-Content -LiteralPath (Join-Path $repositoryRoot 'build/game-ui/layout-official-v1.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $expected = @()
    foreach ($node in $layoutInput.nodes | Where-Object { $_.id -like 'character.stat.*' }) {
        $caption = @($layoutInput.nodes | Where-Object { $_.id -eq ('character.caption.' + $node.bindingId) })
        $pageName = @{character='Character';status='Status';hermit='Hermit'}[$node.page]
        $valueRect = if ($node.valueRect) { $node.valueRect -join ',' } else { $node.rect -join ',' }
        $captionRect = if ($node.captionRect) { $node.captionRect } elseif ($caption.Count -eq 1) { $caption[0].rect } else { $null }
        $icon = '-'; $cap = '-'; $text = '-'
        if ($node.preserveTag) {
            $icon = if ($node.iconRect) { $node.iconRect -join ',' } else { @($captionRect[0],$captionRect[1],16,$captionRect[3]) -join ',' }
        } else {
            if ($captionRect) { $cap = $captionRect -join ',' }
            if ($caption.Count -eq 1) { $text = $caption[0].label }
            elseif ($node.captionText) { $text = $node.captionText }
        }
        $expected += ($node.bindingId + '=' + ($pageName,$valueRect,$cap,$icon,$text -join '|'))
    }
    foreach ($group in @('AttackStats','AdvantageStats','DisadvantageStats')) {
        $node = @($layoutInput.nodes | Where-Object { $_.id -eq ('character.caption.' + $group) })
        if ($node.Count -ne 1) { throw 'Missing element group caption.' }
        $expected += ('group.' + $group + '=' + ($node[0].rect -join ','))
    }
    $fixture = Join-Path $testDirectory 'expected-layout.txt'
    [IO.File]::WriteAllLines($fixture,$expected,[Text.Encoding]::UTF8)
    Copy-Item -LiteralPath $client -Destination $testDirectory
    Get-ChildItem -LiteralPath $clientDirectory -Filter '*.dll' | Copy-Item -Destination $testDirectory
    $executable = Join-Path $testDirectory 'OfficialUiCharacterAttributeTests.exe'
    & $CompilerPath /nologo /target:exe /platform:x86 /r:System.Drawing.dll /r:System.Windows.Forms.dll ('/r:' + $client) ('/r:' + (Join-Path $clientDirectory 'Library.dll')) ('/r:' + (Join-Path $clientDirectory 'SlimDX.dll')) ('/out:' + $executable) (Join-Path $PSScriptRoot 'game-ui\OfficialUiCharacterAttributeTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Character attribute test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'ImageManager\app.config') -Destination ($executable + '.config')
    Push-Location -LiteralPath $testDirectory
    try { & $executable $fixture; if ($LASTEXITCODE -ne 0) { throw 'Character attribute tests failed.' } }
    finally { Pop-Location }
} finally {
    $resolved = [IO.Path]::GetFullPath($testDirectory)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ZirconCharacterAttributeTests-[a-f0-9]{32}$') { throw 'Refusing cleanup outside generated character attribute test directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
