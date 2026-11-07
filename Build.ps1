#requires -Version 5.1
[CmdletBinding()]
param([switch]$RunNativeTests)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
try {
    $workspace = [IO.Path]::GetFullPath($PSScriptRoot)
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'The installed .NET Framework C# compiler is required.' }
    $framework = Get-ItemProperty 'HKLM:/SOFTWARE/Microsoft/NET Framework Setup/NDP/v4/Full' -Name Release
    if ($framework.Release -lt 533320) { throw '.NET Framework 4.8.1 or later is required for this qualified build.' }
    $buildPath = Join-Path $workspace ('.build/build-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $buildPath -Force | Out-Null
    $binary = Join-Path $buildPath 'VaultHelper.exe'
    $tests = Join-Path $buildPath 'VaultHelper.Tests.exe'
    $compilerArguments = @('/nologo','/warn:4','/warnaserror+','/optimize+','/checked+','/platform:anycpu','/target:exe','/r:System.Web.Extensions.dll')
    & $compiler @compilerArguments ('/out:' + $binary) (Join-Path $workspace 'src/VaultCore.cs') (Join-Path $workspace 'src/Program.cs') (Join-Path $workspace 'src/AssemblyInfo.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Helper compilation failed.' }
    & $compiler @compilerArguments ('/out:' + $tests) ('/r:' + $binary) (Join-Path $workspace 'tests/Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    if ($RunNativeTests) { $testOutput = & $tests --native } else { $testOutput = & $tests }
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed; build evidence is retained.' }
    $testResult = $testOutput | ConvertFrom-Json
    $sourceInventory = @()
    foreach ($sourceFile in Get-ChildItem (Join-Path $workspace 'src'),(Join-Path $workspace 'tests') -Filter '*.cs' -Recurse -File | Sort-Object FullName) {
        $sourceInventory += [ordered]@{path=$sourceFile.FullName.Substring($workspace.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    $sourceCommit = $null
    if (Test-Path -LiteralPath (Join-Path $workspace '.git')) {
        $commitProbe = & git -C $workspace rev-parse --verify HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $commitProbe -match '^[a-f0-9]{40}$') { $sourceCommit = $commitProbe.Trim() }
    }
    $info = [ordered]@{schemaVersion=1;version='1.0.0';target='Windows .NET Framework AnyCPU';qualifiedHost=('Windows '+$env:PROCESSOR_ARCHITECTURE);processBits=([IntPtr]::Size*8);osVersion=[Environment]::OSVersion.VersionString;sourceCommit=$sourceCommit;frameworkRelease=$framework.Release;compilerFileVersion=(Get-Item -LiteralPath $compiler).VersionInfo.FileVersion;compilerSha256=(Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash.ToLowerInvariant();tests=$testResult;sources=$sourceInventory;binarySha256=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant();signature='Unsigned build';thirdPartyPackages=@()}
    $info | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $buildPath 'BUILD-INFO.json') -Encoding UTF8
    [ordered]@{buildPath=$buildPath;testsPassed=$testResult.passed;nativeVaultTested=$testResult.nativeVaultTested;binarySha256=$info.binarySha256} | ConvertTo-Json
    exit 0
} catch {
    Write-Error $_.Exception.Message
    exit 1
}
