# MemoryInspector Security and Privacy

## Authorized use

Use MemoryInspector only on software and systems you own, develop, test, administer, or are explicitly authorized to inspect. Process memory can contain credentials, personal information, cryptographic material, proprietary code, and other sensitive data.

MemoryInspector is not designed to bypass operating-system security, anti-tamper controls, protected-process rules, or access-control policy.

## Read-only default

Process exploration, region/module/thread inspection, memory reads, scans, snapshots, Watch, Saved Addresses, Hex Viewer, and Snapshot Compare are read-only with respect to the target process.

The optional Memory Editor is disabled by default. Enabling requires separate risk and authorized-target acknowledgements. Production writes:

- revalidate the complete process/session identity;
- require a committed writable non-Guard/non-NoAccess region;
- optionally compare the original bytes;
- perform one write;
- read back and verify;
- create an audit record.

The application does not elevate privileges, change protection flags, inject code, hook APIs, or implement continuous freeze writes.

## Local data

The host has no built-in telemetry, cloud synchronization, or network service. It stores data locally under `%LOCALAPPDATA%\MemoryInspector`.

Potentially sensitive files include:

- snapshots and scan history;
- Saved Address catalogs and target metadata;
- logs and plugin logs;
- Memory Editor audit records;
- installed plugin binaries and activation state.

These files are not encrypted by MemoryInspector. Windows user-profile permissions and disk encryption are the primary at-rest controls.

## Data minimization

- Delete temporary sessions when no longer needed.
- Export only the minimum necessary rows.
- Review CSV, logs, and audit files before sharing.
- Avoid descriptive Saved Address keys that reveal secrets.
- Keep Watch lists limited to the active investigation.
- Use full-disk encryption on systems handling sensitive targets.

## Plugins

Plugins execute managed code inside the host process. `AssemblyLoadContext` and per-plugin DI improve dependency and lifecycle isolation but do not form a security sandbox. A malicious plugin can use normal .NET/OS capabilities available to the user.

Install only trusted, reviewed plugins. Verify package origin and hashes, keep each plugin in its own directory, and remove disabled plugins that are no longer required.

## Logs and audits

Application logs may contain process identifiers, names, paths, addresses, and error details. Memory Editor audit files intentionally preserve security-relevant operation metadata. Restrict access and retention according to organizational policy.

Temporary Manager does not delete Saved Addresses, logs, plugins, settings, or audit files.

## Release integrity

Official ZIP files include a `.sha256` sidecar. The main package also includes `release-manifest.json` with per-file SHA-256 values. Verify the archive before extraction and obtain updates from a trusted distribution channel.

The Phase 33 portable package is not code-signed. Windows may display reputation warnings until a signing identity and signed installer are introduced. Do not suppress warnings for packages whose origin or hash cannot be verified.

## Incident response

If misuse, an unexpected write, or plugin compromise is suspected:

1. Stop the Monitoring Session and close MemoryInspector.
2. Preserve relevant logs and audit records under controlled access.
3. Record package and plugin hashes.
4. Remove untrusted plugins before reopening.
5. Follow the target system owner's incident-response process.

