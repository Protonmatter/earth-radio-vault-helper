#requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
try {
    $workspace = [IO.Path]::GetFullPath($PSScriptRoot)
    $sourceFiles = @(Get-ChildItem -LiteralPath $workspace -File | Where-Object {$_.Extension -in @('.ps1','.md') -or $_.Name -in @('.gitattributes','.gitignore')})
    foreach ($directory in @('src','tests','docs','.github')) { $sourceFiles += @(Get-ChildItem -LiteralPath (Join-Path $workspace $directory) -Recurse -File) }
    $forbidden = '(?i)sb_secret_[a-z0-9_-]{20,}|re_[a-z0-9_-]{20,200}|(?:ghp_|github_pat_)[a-z0-9_]{20,}|-----BEGIN [A-Z ]*PRIVATE KEY-----|https?://chatgpt[.]com/space/[a-z0-9_-]+|[a-z]:\\Users\\[^\\\s]+\\'
    foreach ($file in $sourceFiles) {
        if ((Get-Content -LiteralPath $file.FullName -Raw) -match $forbidden) { throw ('Source privacy check failed: '+$file.Name) }
        if ($file.Extension -eq '.ps1') {
            $tokens = $null; $parseErrors = $null
            [Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$parseErrors) | Out-Null
            if ($parseErrors.Count -gt 0) { throw ('PowerShell parser failed: '+$file.Name) }
        }
    }
    $buildOutput = & powershell.exe -NoProfile -File (Join-Path $workspace 'Build.ps1') -RunNativeTests
    if ($LASTEXITCODE -ne 0) { throw 'Build/native verification failed.' }
    $build = $buildOutput | ConvertFrom-Json
    $output = Join-Path $build.buildPath 'ci-dist'
    $packageOutput = & powershell.exe -NoProfile -File (Join-Path $workspace 'Package.ps1') -BuildDirectory $build.buildPath -OutputDirectory $output
    if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' }
    $package = $packageOutput | ConvertFrom-Json
    & powershell.exe -NoProfile -File (Join-Path $package.extractedPath 'Verify-Package.ps1') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Extracted manifest failed.' }
    $nativeOutput = & (Join-Path $package.extractedPath 'VaultHelper.Tests.exe') --native
    if ($LASTEXITCODE -ne 0) { throw 'Extracted native tests failed.' }
    Add-Content -LiteralPath (Join-Path $package.extractedPath 'README.md') -Value 'Synthetic corruption test'
    # Failure is expected here and its output contains only a fixed package path.
    $previousPreference = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $rejection = & powershell.exe -NoProfile -File (Join-Path $package.extractedPath 'Verify-Package.ps1') 2>&1
    $rejectionExit = $LASTEXITCODE; $ErrorActionPreference = $previousPreference
    if ($rejectionExit -ne 1) { throw 'Corrupted package was not rejected.' }
    $info = Get-Content (Join-Path $build.buildPath 'BUILD-INFO.json') -Raw | ConvertFrom-Json
    $result = [ordered]@{passed=$true;sourceCommit=$info.sourceCommit;qualifiedHost=$info.qualifiedHost;assertionsPassed=$build.testsPassed;extractedAssertions=($nativeOutput|ConvertFrom-Json).passed;manifestFiles=$package.manifestFiles;corruptionRejected=$true;artifactSha256=$package.sha256;sourceFilesScreened=$sourceFiles.Count;productionCredentialAccessed=$false}
    $result | ConvertTo-Json | Set-Content (Join-Path $build.buildPath 'CI-RESULT.json') -Encoding UTF8
    $result | ConvertTo-Json
    exit 0
} catch {
    Write-Error $_.Exception.Message
    exit 1
}
