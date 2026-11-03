# Download management

FPKGi is for lawful homebrew and owner-supplied FPKGs. It does not provide catalogs, credentials, entitlement bypass, or package content.

## Catalog contract

`size` is required and is the exact unsigned byte length. `sha256` is optional but strongly recommended; when present it must be 64 hexadecimal characters.

```json
{
  "DATA": {
    "https://lan.example/probe.pkg": {
      "title_id": "CUSA00001",
      "region": "ALL",
      "name": "FW14 FPKGi Probe",
      "version": "01.00",
      "release": "01-01-2026",
      "size": "8388608",
      "min_fw": "5.05",
      "cover_url": null,
      "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
    }
  }
}
```

Download URLs must be absolute HTTP or HTTPS URLs without embedded credentials or fragments. HTTPS is authenticated with the bundled Mozilla CA store. Plain HTTP remains available for an explicitly controlled LAN source; use SHA-256 because HTTP does not authenticate the server.

## Direct mode

Direct mode is the verified path:

1. Validate URL, title ID, version, destination root, exact size, and optional SHA-256.
2. Derive one canonical filename under `/user/data`, `/mnt/usbN`, or `/mnt/extN`.
3. Preflight known free space with a 64 MiB reserve.
4. Resume from the `.pkg.resume` partial file.
5. Retry transient transport failures and HTTP 408, 425, 429, 500, 502, 503, and 504 with bounded exponential delay.
6. Wait for native cancellation to finish before reset or another transfer.
7. Accept the final package only when PKG magic, exact size, and optional SHA-256 match.
8. Persist an `installing` journal state before submitting installation.
9. Submit the verified package to the native installer.
10. Persist `completed` after native success, then remove the queue record.

Ordinary 4xx responses, malformed metadata, path failures, insufficient known space, and integrity failures do not retry. Failed and cancelled entries remain in the queue for inspection/retry. Partial files survive transfer failures. `deleteOnCancel` removes only the canonical partial path after native cancellation reaches a terminal state.

ZIP payloads are not supported. A ZIP or other non-PKG response fails verification and is retained; FPKGi never renames, extracts, or deletes it as a successful package.

## Background mode

Background mode persists `submitting` before crossing into the PS4 BGFT service. A zero native result must then persist `submitted`; FPKGi can prove task registration, not package completion or content integrity. If either result is ambiguous across interruption or persistence failure, the item becomes `blocked` and the operator must inspect PS4 notifications/downloads before removing or re-adding it. System notification/BGFT state remains authoritative.

Use direct mode when exact size/SHA-256 verification and in-app retry/resume state are required. Rest-mode behavior differs between process-owned direct downloads and BGFT; qualify both separately.

## Persistent queue

`/user/data/FPKGi/download-queue.json` uses schema version 1 and stores a metadata snapshot, stable content ID, attempts, state, last error, and canonical destination. Queue writes use a same-filesystem temporary file and backup fallback. If interruption leaves the primary absent, startup restores `.bak` first or `.tmp` second before deciding the queue is new. If artifacts exist but cannot be restored, startup fails closed and retains them instead of writing an empty queue.

States visible in the queue:

- `queued`: ready to run;
- `downloading`: native transfer active;
- `retrying`: transient failure waiting for bounded retry;
- `failed`: retained for user retry/removal;
- `submitting`: durable BGFT-submission intent; converted to `blocked` after an interrupted session because task acceptance is unknown;
- `submitted`: BGFT accepted the request; completion is external;
- `installing`: durable local-install intent; converted to `blocked` after an interrupted session because native completion is unknown;
- `completed`: durable local completion marker, removed during normal completion or startup recovery;
- `blocked`: never dispatched automatically; inspect the error, verify console state where required, then remove and re-add deliberately.

A malformed queue is preserved as `download-queue.json.invalid` and replaced with an empty queue. Loaded records are validated and canonicalized before duplicate/path arbitration, so invalid metadata cannot reserve a valid destination. Stable identity uses normalized URL + title ID + version. Reusing that identity is accepted only when the immutable name, exact parsed size, optional normalized SHA-256, and canonical destination also agree; a conflicting snapshot is blocked instead of silently reusing stale metadata.

## Storage and migration

Canonical direct-download filenames are:

```text
TITLEID_VERSION_Sanitized Name.pkg
TITLEID_VERSION_Sanitized Name.pkg.resume
```

The v1.10 form (`[TITLEID] Name.pkg`) is not treated as verified state because it cannot reliably identify duplicate names/versions. Existing files are left untouched. Verify and rename them deliberately, or let this build download them under the canonical name.

Changing the configured destination validates the new root and locks each queued item to its recorded canonical path. Files outside an allowed root are never deleted or installed by queue cleanup.

## Operational diagnostics

For a failed item, record:

- FPKGi and UnityOrbisBridge commit/package digests;
- firmware and exact HEN build;
- queue state, attempt count, HTTP status, and native curl/BGFT code;
- destination and free-space result;
- expected and actual size/SHA-256;
- `/data/UnityOrbisBridge.log` and LAN server logs.

Do not include URL credentials or private catalog contents in public reports.
