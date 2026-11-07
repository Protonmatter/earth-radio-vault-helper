#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$BuildDirectory,
    [string]$ConsoleEvidence,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
try {
    $workspace = [IO.Path]::GetFullPath($PSScriptRoot)
    $build = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $BuildDirectory).Path)
    $buildPrefix = Join-Path $workspace '.build'
    if (-not $build.StartsWith($buildPrefix + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Select a build beneath this source workspace.' }
    $info = Get-Content (Join-Path $build 'BUILD-INFO.json') -Raw | ConvertFrom-Json
    if ($info.version -ne '1.0.0' -or -not $info.tests.nativeVaultTested -or $info.tests.failed -ne 0) { throw 'A verified native-test build is required.' }
    foreach ($source in $info.sources) {
        if ($source.path -notmatch '^(src|tests)[/\\][A-Za-z0-9_.-]+\.cs$') { throw 'Invalid source inventory path.' }
        if ((Get-FileHash -LiteralPath (Join-Path $workspace $source.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $source.sha256) { throw 'Source changed after compilation.' }
    }
    $binary = Join-Path $build 'VaultHelper.exe'
    if ((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $info.binarySha256) { throw 'Compiled binary changed.' }
    $validation = [ordered]@{schemaVersion=1;binarySha256=$info.binarySha256;assertionsPassed=$info.tests.passed;nativeVaultTested=$true;interactiveMaskedStore=$false;interactiveConfirmationMismatch=$false;productionCredentialAccessed=$false}
    if ($ConsoleEvidence) {
        $console = Get-Content -LiteralPath $ConsoleEvidence -Raw | ConvertFrom-Json
        if ($console.binarySha256 -ne $info.binarySha256 -or $console.productionCredentialAccessed) { throw 'Console evidence belongs to another build or scope.' }
        $validation.interactiveMaskedStore = $console.interactiveMaskedStore -eq $true
        $validation.interactiveConfirmationMismatch = $console.interactiveConfirmationMismatch -eq $true
    }
    $payload = Join-Path $buildPrefix ('package-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $payload | Out-Null
    $sources = @('README.md','Build.ps1','Package.ps1','Verify-Package.ps1','Test-CI.ps1','.github/workflows/ci.yml','.gitattributes','.gitignore','src/AssemblyInfo.cs','src/Program.cs','src/VaultCore.cs','tests/Tests.cs','docs/ARCHITECTURE.md','docs/BUILD.md','docs/SECURITY.md','docs/ROLLBACK.md')
    $sourceSums = @()
    foreach ($relative in $sources) {
        $from = Join-Path $workspace $relative
        $to = Join-Path $payload $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
        Copy-Item -LiteralPath $from -Destination $to
        $sourceSums += ((Get-FileHash -LiteralPath $from -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $relative)
    }
    $sourceSums -join "`n" | Set-Content (Join-Path $payload 'SOURCE-SHA256SUMS.txt') -Encoding UTF8
    foreach ($name in @('VaultHelper.exe','VaultHelper.Tests.exe','BUILD-INFO.json')) { Copy-Item -LiteralPath (Join-Path $build $name) -Destination (Join-Path $payload $name) }
    $validation | ConvertTo-Json | Set-Content (Join-Path $payload 'VALIDATION.json') -Encoding UTF8
    $rows = @()
    foreach ($file in Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName) {
        $rows += [ordered]@{path=$file.FullName.Substring($payload.Length+1).Replace([string][IO.Path]::DirectorySeparatorChar,'/');bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    [ordered]@{schemaVersion=1;version='1.0.0';target='Windows .NET Framework AnyCPU';qualifiedHost=$info.qualifiedHost;sourceCommit=$info.sourceCommit;signature='Unsigned build';files=$rows} | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $payload 'PACKAGE-MANIFEST.json') -Encoding UTF8
    & powershell.exe -NoProfile -File (Join-Path $payload 'Verify-Package.ps1') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Staged package verification failed.' }
    $dist = Join-Path $workspace 'dist'
    if ($OutputDirectory) {
        if ([IO.Path]::IsPathRooted($OutputDirectory)) { $dist = [IO.Path]::GetFullPath($OutputDirectory) }
        else { $dist = [IO.Path]::GetFullPath((Join-Path $workspace $OutputDirectory)) }
    }
    $allowedOutput = ($dist -eq (Join-Path $workspace 'dist')) -or $dist.StartsWith((Join-Path $workspace 'dist')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $dist.StartsWith($buildPrefix+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
    if (-not $allowedOutput) { throw 'Package output must stay under dist or .build in this workspace.' }
    New-Item -ItemType Directory -Path $dist -Force | Out-Null
    $zip = Join-Path $dist 'EarthRadioVaultHelper-1.0.0-windows-anycpu.zip'
    $checksums = Join-Path $dist 'SHA256SUMS.txt'
    if ((Test-Path -LiteralPath $zip) -or (Test-Path -LiteralPath $checksums)) { throw 'Distribution already exists; preserve it.' }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipStream = [IO.File]::Open($zip, [IO.FileMode]::CreateNew)
    try {
        $archive = New-Object IO.Compression.ZipArchive($zipStream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($file in Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName) {
                $name = $file.FullName.Substring($payload.Length+1).Replace([string][IO.Path]::DirectorySeparatorChar,'/')
                $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::Parse('1980-01-01T00:00:00+00:00', [Globalization.CultureInfo]::InvariantCulture)
                $input = [IO.File]::OpenRead($file.FullName)
                $output = $entry.Open()
                try { $input.CopyTo($output) } finally { $input.Dispose(); $output.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $zipStream.Dispose() }
    $zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($checksums, $zipHash + '  ' + [IO.Path]::GetFileName($zip) + "`n", (New-Object Text.UTF8Encoding($false)))
    $extracted = Join-Path $buildPrefix ('portable extraction-' + [Guid]::NewGuid().ToString('N'))
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $extracted)
    & powershell.exe -NoProfile -File (Join-Path $extracted 'Verify-Package.ps1') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Extracted ZIP verification failed.' }
    [ordered]@{artifact=$zip;sha256=$zipHash;bytes=(Get-Item -LiteralPath $zip).Length;manifestFiles=$rows.Count;extractedPath=$extracted;interactiveEvidenceIncluded=$validation.interactiveMaskedStore} | ConvertTo-Json
    exit 0
} catch {
    Write-Error $_.Exception.Message
    exit 1
}
