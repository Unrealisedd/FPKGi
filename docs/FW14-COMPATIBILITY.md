# PS4 firmware 14.00 compatibility

## Verdict

The managed FPKGi policy is firmware-agnostic, but the complete application is **not yet hardware-qualified on firmware 14.00**. A defensible compatibility claim requires the real-console matrix below with the exact HEN, FPKGi package, and UnityOrbisBridge digests recorded.

Source review cannot prove HEN kernel patches, Unity 2017 runtime behavior, native module ABI, AppInstUtil/BGFT behavior, dialogs, controller input, or rest-mode behavior.

## Compatibility boundary

The required chain is:

1. A firmware-14.00 HEN launches FSELF/homebrew and applies FPKG/ShellCore patches.
2. The HEN grants the credentials and mounts required by `libjbc` and FPKGi.
3. Unity loads the rebuilt `UnityOrbisBridge.prx`.
4. The bridge initializes message dialogs, AppInstUtil, and BGFT without blocking.
5. FPKGi downloads, verifies, and submits an owner-supplied FPKG.

FPKGi contains no firmware offsets. Firmware-specific FSELF/FPKG and ShellCore patches belong to the HEN layer.

## Primary evidence

- Scene-Collective `ps4-hen` commit [`d077fb4031cf0e089fb7a233cebeebcd1ed3c68e`](https://github.com/Scene-Collective/ps4-hen/commit/d077fb4031cf0e089fb7a233cebeebcd1ed3c68e) adds 14.00 offsets, sets `MAX_FW` to 1400, and extends FPKG/ShellCore patch ranges through 14.00. This is source support, not FPKGi hardware proof.
- Its generated [FW14 pre-release](https://github.com/Scene-Collective/ps4-hen/releases/tag/pre-release-main-182) is a pre-release. Record its exact payload digest; do not label it GoldHEN.
- The official GoldHEN repository has no release that establishes firmware-14.00 support. Do not infer compatibility from a similarly named third-party payload.
- FPKGi issue [#26](https://github.com/ItsJokerZz/FPKGi/issues/26) reports startup/input freezes on newer firmware; [#31](https://github.com/ItsJokerZz/FPKGi/issues/31) records path and stale-BGFT-task failures; [#50](https://github.com/ItsJokerZz/FPKGi/issues/50) reports background-download failure on 13.52 after v1.10.
- Upstream FPKGi v1.10 source had unbounded sandbox/module waits. This fork replaces them with a bounded startup result and visible diagnostic, but only a console can validate the native boundary.

## Required test asset

Use a small owner-built synthetic homebrew FPKG with:

- a unique test title ID;
- no copyrighted assets or entitlement logic;
- a screen displaying a fixed build ID;
- recorded package byte size and SHA-256;
- a one-entry catalog served from a LAN host controlled by the operator.

The catalog entry must include exact `size` and `sha256` values. Keep the package and catalog available over both plain LAN HTTP and trusted HTTPS if HTTPS is being qualified.

## Firmware-14.00 matrix

Record console model, firmware (`14.00`), HEN repository/commit or release, HEN payload SHA-256, FPKGi commit/package SHA-256, bridge commit/PRX SHA-256, storage target, and LAN server logs.

| Scenario | Pass criteria |
|---|---|
| Cold start, offline | Main UI becomes interactive within 30 seconds. Log records firmware, sandbox access, native-service initialization, and completion. Failure shows a stage/error instead of a black screen or endless wait. |
| First run | `/user/data/FPKGi/Backgrounds`, `ContentJSONs`, `Downloads`, `config.json`, and `download-queue.json` are created under the expected root. Relaunch preserves config and queue. |
| Controller and IME | D-pad/sticks, X, Circle, Triangle, Square, L1/R1, L2/R2, R3, and touchpad search produce their documented state transitions; native keyboard opens and closes. |
| LAN catalog | The server observes the JSON request; the single probe entry appears exactly once; offline update-service failure does not disable the configured LAN catalog. |
| Direct download/install | Server sends the exact bytes; UI reaches terminal success; final file size/SHA-256 match; no `.resume` remains; local install returns success; the probe launches and displays its build ID. |
| Cancel/resume | Cancel during body transfer reaches native `Cancelled` before UI reset. A retained `.resume` is resumed on retry; `deleteOnCancel` removes only that validated partial path. Final digest still matches. |
| Retry | Controlled 503 responses produce bounded exponential retries and preserve the queue item. A 404 does not retry. A later successful response completes exactly once. |
| Integrity failure | A same-size tampered package and a truncated package are both retained as failed and are never submitted to AppInstUtil/BGFT. |
| Queue recovery | Power-cycle/relaunch with queued, retrying, failed, `submitting`, `installing`, and `completed` records. Download records reappear with metadata and state; `submitting` and `installing` become `blocked` for manual PS4-state verification; `completed` is removed and never submitted twice. Interrupt an atomic queue/config replacement and confirm `.bak` or `.tmp` recovery; make recovery fail and confirm startup retains artifacts and stops. |
| Background/BGFT | A clean notification queue accepts the task. FPKGi durably records `submitting` before the native call and `submitted` after acceptance, never installed. The system task completes, the probe installs and launches, and native registration/persistence errors remain visible without automatic resubmission. |
| Duplicate/stale task | Reinstall once, then cancel a background task and retry after clearing its notification. No crash, false success, or permanent orphan task. |
| Rest mode, BGFT | During a throttled BGFT transfer, one rest/resume cycle either continues or reports a recoverable native state; final install and launch succeed without an orphan task. |
| Rest mode, direct | Test separately. Relaunch resumes the partial file and verifies the final digest. BGFT success does not imply direct-transfer success. |

External USB/extended storage is optional qualification. If claimed, repeat direct transfer, free-space preflight, install, and cleanup on that exact mount.

## Allowed claim

After every required row passes on the real console, use a narrow statement:

> This FPKGi build passed the documented owner-supplied synthetic-homebrew matrix on PS4 firmware 14.00 with the recorded HEN and bridge builds. The result does not cover other firmware-14.00 HEN variants.

Until then, label firmware 14.00 **experimental / hardware validation required**.
