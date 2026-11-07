# Earth Radio Vault Helper 1.0.0

A separate Windows console tool that creates one Generic credential in the current
Windows user's Credential Manager, verifies readback, and writes a metadata-only
receipt. It refuses an observed existing target, including an empty credential.
Secret entry and confirmation use a masked console; there is no secret argument,
environment, file, stdin, reveal or general delete option.

The ZIP includes executables, complete C# source, tests, PowerShell build/verification
scripts and operating documentation. It requires .NET Framework 4.8.1. No Node, npm,
SDK download, installer or elevation is needed on the qualified Windows 11 ARM64 host.
The binary is AnyCPU and unsigned. CI exercises Windows x64 and ARM64; each release's
provenance records completed coverage and the actual build host.

Source: [Protonmatter/earth-radio-vault-helper](https://github.com/Protonmatter/earth-radio-vault-helper).
Downloads: [v1.0.0 release](https://github.com/Protonmatter/earth-radio-vault-helper/releases/tag/v1.0.0).

## Verify and run

Download the ZIP and SHA256SUMS.txt from the same release, then compare the ZIP's
SHA-256 before extracting. BUILD-PROVENANCE.json records the source commit and CI:

```powershell
Get-FileHash -LiteralPath .\EarthRadioVaultHelper-1.0.0-windows-anycpu.zip -Algorithm SHA256
```

From the extracted directory:

```powershell
powershell.exe -NoProfile -File .\Verify-Package.ps1
.\VaultHelper.exe --help
```

Checksums establish file equality, not a trusted publisher identity. Keep OS security
and execution policy enabled; review source or follow organizational approval if blocked.

## Store a new credential

Choose a new application-specific target and an account identifier. These are metadata,
not secrets. Allowed characters are ASCII letters/digits and `. _ / @ : + -`, with an
initial letter/digit and 1-128 characters. The two helper test namespaces are reserved.

```powershell
$vaultReceiptDirectory = Join-Path $env:LOCALAPPDATA 'EarthRadioVaultHelper\receipts'
New-Item -ItemType Directory -Path $vaultReceiptDirectory -Force | Out-Null
$vaultReceipt = Join-Path $vaultReceiptDirectory ([Guid]::NewGuid().ToString('N') + '.json')
.\VaultHelper.exe store --target ExampleApp/SMTP --account sender --receipt $vaultReceipt
```

Enter the secret twice when prompted. Generic format preserves whitespace and accepts
a single line of Unicode up to 512 UTF-16 code units. NUL, CR/LF and malformed input
are rejected. Optional `--format resend` checks key shape only, not online validity/scope.

```powershell
.\VaultHelper.exe probe --target ExampleApp/SMTP
```

Probe reports presence only and does not create a placeholder. The explicit target is
the actual Windows Generic credential name. The old Node helper's service/account
mapping is not migrated. This delivery leaves Earth Radio's existing SMTP entry intact.

Exit 0 means success; exit 1 means failure/cancellation. Final stdout is JSON; prompts
and mask characters use stderr. A failed operation may still have stored a credential:
inspect `stored`, `verified` and `receiptWritten`. `stored:null` means uncertain.

See [BUILD](docs/BUILD.md), [ARCHITECTURE](docs/ARCHITECTURE.md),
[SECURITY](docs/SECURITY.md) and [ROLLBACK](docs/ROLLBACK.md).
