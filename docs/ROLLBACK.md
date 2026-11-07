# Rollback and removal

## Tool rollback

Close helper/test processes, preserve metadata receipts needed for audit, and remove
only the explicitly identified extracted directory after verifying its resolved path.
Nothing is installed or registered. For rollback, extract a previously verified ZIP
to another directory and validate its hash/manifest before use. Preserve build evidence.

Removing the package leaves Credential Manager entries and provider SMTP settings
intact. This delivery does not replace Earth Radio, read its SMTP key, migrate its
existing vault entry or change its providers.

## Credential rollback

There is no general delete, overwrite, export or rotation command. E_EXISTS preserves
the prior target and needs no rollback. Keep receipts for new targets and use probe
to check presence. A nonzero result can still mean storage happened; inspect its flags.

Before intentionally removing a new credential, establish another usable authorized
copy or a provider-approved recovery path. Review the exact Generic target in Windows
Credential Manager and remove only that entry through the owner's normal workflow.
The metadata receipt cannot restore a secret. Removing a local backup does not revoke
a provider key or undo server SMTP settings. Provider rotation is a separate change.

## Test cleanup

Native tests use random GUID targets under EarthRadioVaultHelper.SelfTest/ and verify
deletion in finally. The tests-only cleanup option rejects other target shapes. If
forcibly interrupted, inspect nonsecret test evidence and remove only that exact
reserved test target. Never store a real credential in a test namespace.

Two empty factory probes created while inspecting the old binding were removed with
exact namespace/GUID/account/empty-blob/time guards and absence readback. No production
credential was accessed. The one-time cleanup script/identifiers are excluded from ZIPs.
