# Shared build/deployment helpers. Dot-source; no persistent or runtime configuration.
Set-StrictMode -Version Latest
function Get-GameUiFullPath([string]$Value) {
    $full = [IO.Path]::GetFullPath($Value)
    if ($full -eq [IO.Path]::GetPathRoot($full)) { return $full }
    return $full.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}
function Test-GameUiWithin([string]$Child, [string]$Parent) {
    return $Child.Equals($Parent, [StringComparison]::OrdinalIgnoreCase) -or
        $Child.StartsWith($Parent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}
function Assert-GameUiOrdinaryPath([string]$Value) {
    for ($current = $Value; $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if ((Test-Path -LiteralPath $current) -and
            (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw "Reparse points are not supported in deployment paths: $current"
        }
    }
}
function Get-GameUiFileRecord([string]$FilePath, [string]$RelativePath) {
    $item = Get-Item -LiteralPath $FilePath -ErrorAction Stop
    if ($item.PSIsContainer) { throw "Expected a file: $FilePath" }
    return [pscustomobject][ordered]@{ path = $RelativePath; length = $item.Length;
        sha256 = (Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash.ToLowerInvariant() }
}
function Get-GameUiTextHash([string]$Text) {
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))).Replace('-', '').ToLowerInvariant() }
    finally { $hasher.Dispose() }
}
function Get-GameUiContractHash($Contract) {
    $rows = @('gameui-contract-v1', ([bool]$Contract.runtimeReady).ToString().ToLowerInvariant(),
        $Contract.packageRelativePath, $Contract.packageSha256, $Contract.sourceManifestSha256)
    foreach ($frame in @($Contract.frames | Sort-Object index)) {
        $rows += ('frame|{0}|{1}|{2}|{3}|{4}|{5}|{6}' -f $frame.id, $frame.index, $frame.width, $frame.height, $frame.offsetX, $frame.offsetY, $frame.sha256)
    }
    foreach ($button in @($Contract.buttons | Sort-Object id)) {
        $rows += ('button|{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}' -f $button.id, $button.normal, $button.hover, $button.pressed, $button.disabled,
            $button.normalTint, $button.hoverTint, $button.pressedTint, $button.disabledTint)
    }
    return Get-GameUiTextHash ($rows -join [char]10)
}
function Get-GameUiBuildId($Manifest) {
    $rows = @('gameui-release-v1', $Manifest.style, $Manifest.catalogContractSha256)
    foreach ($file in @($Manifest.client, $Manifest.package) + @($Manifest.dependencies | Sort-Object path)) {
        $rows += ('{0}|{1}|{2}' -f $file.path, $file.length, $file.sha256)
    }
    return 'gameui-' + (Get-GameUiTextHash ($rows -join [char]10))
}
function Invoke-GameUiContractInspection([string]$ClientDirectory, [string]$PackagePath) {
    $windowsPowerShell = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
    if (![Environment]::Is64BitOperatingSystem) { $windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe' }
    if (!(Test-Path -LiteralPath $windowsPowerShell -PathType Leaf)) { throw 'x86 Windows PowerShell is required for .NET Framework client inspection.' }
    $inspectionFile = Join-Path ([IO.Path]::GetTempPath()) ('ZirconUiInspection-' + [guid]::NewGuid().ToString('N') + '.json')
    $stdout = $inspectionFile + '.stdout.log'
    $stderr = $inspectionFile + '.stderr.log'
    try {
        $arguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File',
            ('"' + (Join-Path $PSScriptRoot 'Read-GameUiReleaseContract.ps1') + '"'),
            '-ClientDirectory', ('"' + $ClientDirectory + '"'), '-PackagePath', ('"' + $PackagePath + '"'), '-OutputPath', ('"' + $inspectionFile + '"'))
        $process = Start-Process -FilePath $windowsPowerShell -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        if ($process.ExitCode -ne 0) {
            $detail = [IO.File]::ReadAllText($stderr)
            throw "Compiled client/catalog/package inspection failed: $detail"
        }
        return Get-Content -LiteralPath $inspectionFile -Raw | ConvertFrom-Json
    } finally {
        foreach ($temporaryFile in @($inspectionFile, $stdout, $stderr)) {
            if (Test-Path -LiteralPath $temporaryFile -PathType Leaf) { Remove-Item -LiteralPath $temporaryFile -Force }
        }
    }
}
function Write-GameUiJsonAtomically([string]$OutputPath, $Document, [switch]$NoOverwrite) {
    Assert-GameUiOrdinaryPath $OutputPath
    $directory = [IO.Path]::GetDirectoryName($OutputPath)
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporary = Join-Path $directory ('.gameui-' + [guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporary, ($Document | ConvertTo-Json -Depth 16) + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
        if (Test-Path -LiteralPath $OutputPath) {
            if ($NoOverwrite) { throw "Output already exists: $OutputPath" }
            [IO.File]::Replace($temporary, $OutputPath, [System.Management.Automation.Language.NullString]::Value)
        } else { [IO.File]::Move($temporary, $OutputPath) }
    } finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) { Remove-Item -LiteralPath $temporary -Force }
    }
}
