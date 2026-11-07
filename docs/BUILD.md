# Source and build instructions

## Source map

| File | Purpose |
| --- | --- |
| src/Program.cs | CLI, masked input/confirmation, buffer clearing and sanitized results |
| src/VaultCore.cs | Policy, named mutex, native Windows adapter and metadata receipts |
| src/AssemblyInfo.cs | Version metadata |
| tests/Tests.cs | Failure cases, disposable native tests and cross-process contention |
| Build.ps1 | Compile with the installed Framework compiler and run tests |
| Package.ps1 | Verify source/build identity and make the source/binary ZIP |
| Verify-Package.ps1 | Verify manifest-listed files by SHA-256 |
| Test-CI.ps1 | Privacy/parser checks, native tests, packaging, extraction and corruption rejection |
| .github/workflows/ci.yml | Read-only standard Windows x64/ARM64 CI with pinned checkout |

## Build

Use Windows PowerShell 5.1 and .NET Framework 4.8.1 with its installed Framework64 C#
compiler. The original local host is Windows 11 ARM64, Framework release 533509. No NuGet
restore, new dependency, paid service, administrative privilege or network is required.

```powershell
powershell.exe -NoProfile -File .\Build.ps1 -RunNativeTests
```

Each run makes a new `.build\build-<GUID>` directory. Compiler switches select C# 5,
AnyCPU, optimization, checked arithmetic, warning level 4 and warnings as errors.
Only Framework assemblies are referenced. Default Build.ps1 runs unit/process checks;
RunNativeTests additionally writes/removes disposable test credentials. BUILD-INFO.json
records compiler version/hash, measured host, source commit/hashes, binary hash and
test results. An uncommitted source copy has no sourceCommit identity.

Pass the exact directory returned by build stdout to packaging:

```powershell
powershell.exe -NoProfile -File .\Package.ps1 -BuildDirectory .\.build\build-<GUID>
```

Packaging refuses stale C# source or a changed binary. It includes an explicit file
allowlist and excludes test profiles, actual vault receipts and other .build files.
The ZIP and external SHA256SUMS.txt go under dist; existing output is preserved.
Use -OutputDirectory beneath this workspace's .build or dist for another retained
build. -ConsoleEvidence accepts build-bound metadata from separate interactive tests;
CI does not claim an interactive console test. ZIP
entries are sorted with fixed timestamps. The installed legacy compiler has no
deterministic switch, so independent compilations are not claimed byte-identical.

## Validation

The native suite covers invalid identifiers/secret encodings,
empty/nonempty existing values, read/write/readback and receipt failures, Unicode,
cooperating-thread exclusion, two-process contention and exact native cleanup. A
pseudoterminal smoke exercised masked entry/confirmation and real store/readback.
Mismatched confirmation failed before creating a credential or receipt in the original
local qualification. Publication adds a regression that denies a lookalike test
cleanup namespace. Consult BUILD-INFO.json for the exact build's assertion count.

```powershell
.\VaultHelper.Tests.exe --native
```

Native tests use only random GUID targets under EarthRadioVaultHelper.SelfTest/ and
verify removal in finally. Their metadata lives in test-evidence. Never use real
credentials in a test namespace. A tests-only cleanup action accepts only an exact
reserved self-test GUID; the production CLI has no delete command.

## Continuous integration and release

```powershell
powershell.exe -NoProfile -File .\Test-CI.ps1
```

CI runs that same entry point on standard Windows x64 and ARM64 GitHub-hosted runners.
It screens publication source for common credential/private-path patterns, parses
PowerShell, builds with real native tests, verifies a ZIP after extraction to a path
with spaces, reruns native tests there, and requires a corrupted copy to be rejected.
The workflow uses read-only contents access, an immutable checkout action pin and no
project secrets, caches or uploaded Actions artifacts. Results remain in the job logs.

Publish only after the exact source commit's matrix is green. Create an annotated tag,
build a clean local release from that commit, and record its source/compiler/artifact
hashes and CI identity in BUILD-PROVENANCE.json. Upload only explicit release assets,
download them and verify complete sizes/hashes before reporting publication complete.

## Original helper provenance

The first helper was PowerShell plus `@napi-rs/keyring` 1.3.0. Original source hashes:

- Store-EarthRadioSmtpKey.ps1: 49d70c82629c4baf37a533e2c9b84fbf91e2a63f05a2720ca0e3b217084cf0b6
- store-smtp-vault.mjs: 40b64a22377ad1cc7abd42755b79d8fac59862d86b3efe7f61295c6f5aff02c6

The original source and production receipt remain in Earth Radio's ignored
qualification directory. They are not runtime dependencies or package contents.
