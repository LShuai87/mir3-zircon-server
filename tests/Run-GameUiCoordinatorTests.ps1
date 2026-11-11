# Exercises production coordinator geometry without game assets or a running client.
[CmdletBinding()]
param([string]$CompilerPath)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (!$CompilerPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (!(Test-Path -LiteralPath $vswhere)) { throw 'Pass -CompilerPath for the Visual Studio C# compiler.' }
    $CompilerPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw 'C# compiler not found.' }
$testDirectory = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('ZirconGameUiCoordinatorTests-' + [guid]::NewGuid().ToString('N'))))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    $executable = Join-Path $testDirectory 'OfficialUiCoordinatorTests.exe'
    & $CompilerPath /nologo /target:exe /r:System.Drawing.dll ('/out:' + $executable) (Join-Path $repositoryRoot 'Client\Envir\GameUiLayoutCoordinator.cs') (Join-Path $PSScriptRoot 'game-ui\OfficialUiCoordinatorTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Official UI coordinator test compilation failed.' }
    & $executable
    if ($LASTEXITCODE -ne 0) { throw 'Official UI coordinator tests failed.' }
} finally {
    $resolvedTestDirectory = [IO.Path]::GetFullPath($testDirectory)
    $resolvedTempDirectory = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolvedTestDirectory.StartsWith($resolvedTempDirectory, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedTestDirectory) -notmatch '^ZirconGameUiCoordinatorTests-[a-f0-9]{32}$') {
        throw 'Refusing cleanup outside the generated coordinator test directory.'
    }
    Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force
}
