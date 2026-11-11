# Pure persistence tests against the built Client assembly; no GPU, sockets or login.
[CmdletBinding()]
param([string]$CompilerPath, [string]$EvidenceDirectory)
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
if ($EvidenceDirectory) {
    $EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
    if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Use a fresh evidence directory.' }
}
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ZirconChatStoreTests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    Copy-Item -LiteralPath $client -Destination $testDirectory
    Get-ChildItem -LiteralPath $clientDirectory -Filter '*.dll' | Copy-Item -Destination $testDirectory
    $executable = Join-Path $testDirectory 'OfficialUiChatStoreTests.exe'
    & $CompilerPath /nologo /target:exe /platform:x86 /r:System.Drawing.dll /r:System.Web.Extensions.dll ('/r:' + $client) ('/out:' + $executable) (Join-Path $PSScriptRoot 'game-ui\OfficialUiChatStoreTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Chat store test compilation failed.' }
    & $executable $testDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Chat store tests failed.' }
    if ($EvidenceDirectory) {
        [IO.Directory]::CreateDirectory($EvidenceDirectory) | Out-Null
        Copy-Item -LiteralPath (Join-Path $testDirectory 'chat-store-results.txt') -Destination $EvidenceDirectory
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'tests\game-ui\OfficialUiChatStoreTests.cs') -Destination $EvidenceDirectory
    }
} finally {
    $resolvedTestDirectory = [IO.Path]::GetFullPath($testDirectory)
    $expectedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolvedTestDirectory.StartsWith($expectedTemp, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedTestDirectory) -notlike 'ZirconChatStoreTests-*') { throw 'Refusing unexpected cleanup target.' }
    Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force
}
