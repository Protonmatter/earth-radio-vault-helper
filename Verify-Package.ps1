#requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
try {
    $packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
    $manifest = Get-Content (Join-Path $packageRoot 'PACKAGE-MANIFEST.json') -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.version -ne '1.0.0') { throw 'Unsupported package manifest.' }
    $seen = @{}
    foreach ($item in $manifest.files) {
        if ($item.path -notmatch '^[A-Za-z0-9_./-]+$' -or $item.path.Contains('..') -or $item.path.StartsWith('/') -or $seen.ContainsKey($item.path)) {
            throw 'Invalid or duplicate manifest path.'
        }
        $seen[$item.path] = $true
        $file = [IO.Path]::GetFullPath((Join-Path $packageRoot $item.path))
        if (-not $file.StartsWith($packageRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escaped the package.' }
        $fileItem = Get-Item -LiteralPath $file
        if ($fileItem.PSIsContainer -or ($fileItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Expected a regular packaged file.' }
        if ($fileItem.Length -ne $item.bytes -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha256) {
            throw ('Packaged file mismatch: ' + $item.path)
        }
    }
    [ordered]@{version=$manifest.version;verifiedFiles=$seen.Count;publisherTrustEstablished=$false} | ConvertTo-Json
    exit 0
} catch {
    Write-Error $_.Exception.Message
    exit 1
}
