# Security model and limitations

## Flow

1. Parse nonsecret target/account/receipt metadata. Refuse SYSTEM, redirected store
   input, invalid identifiers, existing receipts, remote paths and reparse-point parents.
2. Probe the exact Generic target through CredReadW. Only ERROR_NOT_FOUND means
   absent; other native failures stop. An empty existing value is still present.
3. Read and confirm with Console.ReadKey(intercept=true), displaying masks only.
4. Recheck under a named mutex derived from user SID and case-folded target. Reserve
   a new receipt with CreateNew before attempting a vault write.
5. Write a Generic credential with UTF-16LE data and current-user LocalMachine
   persistence. Pin the mutable input only for the native call.
6. Read back, compare account/persistence/length and every byte, clear the native
   copy before CredFree, and write success metadata. Clear input arrays in finally.

Windows provides credential protection; the helper implements no encryption/key.
LocalMachine persistence means this user's logons on this computer, not all users
or SYSTEM. Consumers must understand UTF-16LE Generic blob encoding. The first
helper's library-generated service/account identity is not migrated.

## Boundaries

- No application network client, telemetry, provider call, installer, service, task,
  security-setting change or rotation. There is no secret reveal/export operation.
- Receipts contain target/account metadata, never a secret or secret-derived hash.
  Keep metadata private when its identifiers are sensitive.
- The mutex coordinates this helper only. CredWriteW has no atomic create-if-absent
  contract; another application can race the final check. Use a new application-owned
  target and avoid concurrent external writers. Arbitrary same-user code is not isolated.
- Malware, administrators, input capture, debuggers, OS paging and crash dumps exceed
  the boundary. Clearing buffers reduces lifetime; OS/JIT copy elimination is not promised.
- This is an unsigned build. Hashes establish equality, not publisher trust.
- Windows/Framework servicing remains an OS responsibility. Other architectures,
  versions, policy-controlled sessions and unavailable stores need separate qualification.

## Failure handling

Exit 0 is success; exit 1 is failure/cancellation. Results use fixed codes and numeric
native error values; raw exception text/entered data is never returned by the CLI.

| Code | Meaning and response |
| --- | --- |
| E_EXISTS | Existing credential preserved; choose a new target |
| E_BUSY | Cooperating writer holds the mutex; retry after it exits |
| E_INTERACTIVE | Store needs a console; do not pipe a secret |
| E_SECRET / E_CONFIRMATION / E_CANCELLED | Input stopped before storage |
| E_VAULT_READ | Read failed, not treated as absence |
| E_VAULT_WRITE | Write outcome uncertain; inspect presence before another action |
| E_VERIFY_AFTER_STORE / E_VAULT_VERIFY | Storage occurred, verification failed; preserve state |
| E_RECEIPT_RESERVE | No write attempted because receipt reservation failed |
| E_RECEIPT_AFTER_STORE | Stored/verified, but receipt failed; preserve the entry |

A failed operation may leave an empty reserved receipt. A receipt cannot reconstruct
the secret. Do not automatically delete, overwrite or rotate after uncertain outcomes.

## References

- [CredReadW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credreadw)
- [CredWriteW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew)
- [CREDENTIALW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/ns-wincred-credentialw)
- [.NET Framework and Arm64](https://learn.microsoft.com/en-us/dotnet/framework/whats-new/)
- [Original binding at v1.3.0](https://raw.githubusercontent.com/Brooooooklyn/keyring-node/v1.3.0/src/entry.rs)
