# Validate proposed geometry, generate twice, compare every output and compile/read back C#.
[CmdletBinding()]
param([string]$CompilerPath, [string]$PythonPath = 'python')
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (!$CompilerPath) { $CompilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath $CompilerPath)) { throw 'C# compiler not found; pass -CompilerPath.' }
$testDirectory = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('ZirconUiLayoutContract-' + [guid]::NewGuid().ToString('N'))))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    $generator = Join-Path $repositoryRoot 'build\game-ui\Validate-OfficialUiLayout.py'
    $first = Join-Path $testDirectory 'first'
    $second = Join-Path $testDirectory 'second'
    & $PythonPath $generator --guards --output $first
    if ($LASTEXITCODE -ne 0) { throw 'First geometry generation failed.' }
    & $PythonPath $generator --guards --output $second
    if ($LASTEXITCODE -ne 0) { throw 'Repeat geometry generation failed.' }
    $files = @(Get-ChildItem -LiteralPath $first -File -Recurse)
    if ($files.Count -ne @(Get-ChildItem -LiteralPath $second -File -Recurse).Count) { throw 'Generated file sets differ.' }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($first.Length + 1)
        $other = Join-Path $second $relative
        if (!(Test-Path -LiteralPath $other) -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $other -Algorithm SHA256).Hash) {
            throw ('Generation is not deterministic: ' + $relative)
        }
    }
    $executable = Join-Path $testDirectory 'OfficialUiLayoutContractTests.exe'
    & $CompilerPath /nologo /target:exe /r:System.Drawing.dll ('/out:' + $executable) (Join-Path $first 'OfficialUiLayout.Generated.cs') (Join-Path $PSScriptRoot 'game-ui\OfficialUiLayoutContractTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Generated C# failed to compile.' }
    & $executable
    if ($LASTEXITCODE -ne 0) { throw 'Generated C# readback failed.' }
    Write-Output ('PASS: all ' + $files.Count + ' generated files byte-identical.')
} finally {
    $resolvedTestDirectory = [IO.Path]::GetFullPath($testDirectory)
    $resolvedTempDirectory = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolvedTestDirectory.StartsWith($resolvedTempDirectory, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedTestDirectory) -notmatch '^ZirconUiLayoutContract-[a-f0-9]{32}$') {
        throw 'Refusing cleanup outside the generated layout contract test directory.'
    }
    Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force
}
