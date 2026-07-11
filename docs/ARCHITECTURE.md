# Architecture

## Goal
Faster, more reliable, more understandable alternative to Snappy Driver Installer (SDI). Core complaints being solved: slow/blocking startup, unclear/dated UI, silent failures to launch.

## Pipeline
1. Hardware + Installed Driver Scanner (SetupAPI / ConfigManager) — enumerates devices, current INF, driver version/date, HWID and Compatible ID chains. Runs off the UI thread, target <1s for typical machine.
2. Local cache (SQLite) — last known-good scan + match results, loaded and rendered immediately on startup before any network call.
3. Signed driver metadata index (GitHub repo) — sharded JSON by device class/HWID prefix. Never contains actual driver binaries. Each shard signed with a release key (Ed25519); client verifies signature before trusting the shard. Fields per entry: hwids[], compatible_ids[], oem/provider, version, release_date, os.min_build, os.arch[], source.url (official OEM/MS Catalog only), source.sha256, source.authenticode_publisher, risk_level.
4. Compatibility & Risk Engine — deterministic ranking:
   1. Exact Hardware ID match
   2. Compatible ID (fallback only)
   3. OS build/edition/arch compatibility
   4. Signed package (OEM or Microsoft) required by default
   5. Version/date comparison (System.Version semantics — newer isn't always better; index carries release_date and known-good flags)
   6. Critical device classes (storage, chipset, GPU, network) require stricter confirmation (signed + no open regressions flagged in index)
5. UI (WPF + WPF-UI, MVVM) — default view: grouped-by-category device list with status badges (up to date / update available / missing / unknown) and one bulk "Update recommended" action. Every row is expandable to Current → Proposed with source and reason — never a black-box single button hiding what changes.
6. Preflight — before any install: verify admin elevation, verify package hash against index-recorded SHA-256, verify Authenticode signature + certificate chain against .cat, block on mismatch.
7. Snapshot — `pnputil /export-driver` for the affected device(s) + metadata snapshot (device instance ID, current INF, provider, version, HWID, exported package path) written to snapshot store. WMI System Restore point created as a supplementary (not sole) safety net — app checks whether System Restore is enabled and warns the user explicitly if it's off.
8. Install — `pnputil /add-driver <inf> /install` for the target device. OEM EXE installers are NOT part of the automated v1 pipeline — flagged as a manual, explicitly-warned mode only (per-vendor silent flags are undocumented/unstable and are the least predictable part of this class of tool).
9. Verify — confirm device now reports no error code / correct driver version, then write an entry to the operation log (structured, timestamped, includes before/after state).
10. Rollback — separate service, not just "hope the restore point works": reinstalls the snapshotted INF, re-triggers device re-enumeration, writes rollback outcome to the log. Available from the UI as a single explicit action tied to a specific past operation.

## Why WPF + MVVM, not WebView2 + React (v1 decision)
- Existing working WPF-UI (Fluent) foundation and experience from a prior project (WinTune) — reuses proven patterns instead of introducing a new UI runtime for a first release.
- WPF-UI gives genuine Fluent/Mica visuals — the "outdated" complaint about SDI is a control-library competence problem, not a WPF-vs-web problem.
- Avoids a Node build step, a C#↔JS IPC bridge, and WebView2's web security surface — fewer moving parts for a tool that runs elevated and touches the driver store.
- React/WebView2 stays an open v2 option if WPF-UI's control set becomes a real limitation for the comparison/review screens.
- Cold start: WPF does not currently support .NET Native AOT (XAML loading and COM interop aren't AOT-trim-compatible as of .NET 8). Use self-contained, ReadyToRun-compiled, single-file publish instead — bundles the runtime (no separate install to fail on) and gets most of the cold-start win without an unsupported AOT path.

## Reliability practices (why "sometimes doesn't even open" doesn't happen here)
- Self-contained + ReadyToRun single-file publish — no external runtime dependency race.
- No self-extracting temp archives on startup — this is the pattern that trips AV heuristics on tools like SDI.
- Global unhandled-exception handler at the composition root writes a structured crash log to `%LOCALAPPDATA%\DriverLens\logs\` and shows a dialog with a "copy log" button — every failure is diagnosable.
- `app.manifest` requests `requireAdministrator` directly (not a runtime elevation request) — UAC prompt is immediate and the UI shows an explicit "waiting for UAC confirmation" state so it's never silently lost behind another window.
- OV code-signing certificate + GitHub Actions reproducible build + winget distribution — reduces SmartScreen friction over time.

## GitHub index trust model
- GitHub repo holds ONLY the signed metadata index — never driver binaries, never arbitrary vendor `.exe` links beyond the recorded official source URL.
- Every shard is signed with a release private key (kept outside the repo); the client embeds the corresponding public key and refuses to load an unsigned or badly-signed shard.
- CI on every PR to the index: fetch the file at `source.url`, recompute SHA-256, compare to the claimed value, verify Authenticode signature/publisher, reject on mismatch. This catches a compromised contributor swapping a URL; the release-key signing (kept off GitHub) is meant to contain the harder case of a compromised GitHub admin account.
