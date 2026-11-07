# Architecture and original helper

The first SMTP helper used PowerShell `Read-Host -AsSecureString`, converted that value
briefly in memory, sent it through the child process's anonymous stdin pipe, and used
the already installed `@napi-rs/keyring` 1.3.0 Node binding to store/read back the secret.
It excluded the key from arguments, environment variables, files and receipts. The
original source stays in Earth Radio's ignored qualification directory; it is not a
dependency of this build.

This separate build uses a C# console executable and the Windows credential APIs.
Inspection of the pinned binding found that its explicit-target factory writes an
empty Windows credential and its password-read wrapper discards native read errors.
Those behaviors are unsuitable for the new explicit-target/create-only contract.
The original production helper used the ordinary constructor, not that factory.
See [the pinned source](https://raw.githubusercontent.com/Brooooooklyn/keyring-node/v1.3.0/src/entry.rs).

## Design choices

A bundled PowerShell/Node package would preserve the first implementation but add a
runtime/native dependency and need another native layer to distinguish read failures.
A direct Windows API executable keeps exact error handling and a smaller package.
The chosen implementation targets the already installed .NET Framework and uses no
third-party runtime package, network API, installer or background service.

`Program.cs` parses nonsecret metadata and reads/clears two masked mutable inputs.
`VaultCore.cs` validates policy, serializes cooperating writers with a SID-scoped
named mutex, refuses an observed existing target, reserves a new receipt, writes a
Generic credential and compares readback. Native credential copies are cleared
before `CredFree`. The JSON result distinguishes pre-write failure, uncertain write,
verification failure and receipt failure after a verified store.

Windows' credential store performs persistence/protection. The helper does not
implement encryption or manage an encryption key. Its explicit target is the actual
Windows Generic credential name; it does not migrate the first helper's library-
generated service/account mapping. Real Earth Radio credentials are not used by tests.
