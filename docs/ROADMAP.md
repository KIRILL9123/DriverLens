# Roadmap

## Phase 0 — Scaffolding
- [x] Solution structure, WPF-UI shell, docs skeleton, git init.

## Phase 1 — Scanner + Matching MVP
- [x] SetupAPI wrapper (device enumeration, HWID/Compatible ID chains, current driver info).
- [x] Compatibility & Risk Engine implementing the 6-step ranking against a local, hand-authored JSON fixture (no real GitHub index yet).
- [x] Read-only WPF-UI list: grouped by category, status badges, Current → Proposed expandable detail. No install actions yet.

## Phase 2 — Signed index + sync
- [x] Index shard format finalized, ECDSA P-256 signing script, public key embedded in client.
- [x] SQLite local cache, background sync with ETag/If-Modified-Since, instant-first-paint from cache.
- [x] CatalogSearch dev CLI tool for automated catalog lookup, verification, and net.json JSON template generation.

## Phase 3 — Install pipeline
- [x] Preflight: admin check, SHA-256 verify (Authenticode/cert-chain verify deferred to Phase 3.5).
- [x] Snapshot: `pnputil /export-driver` + metadata snapshot store.
- [x] WMI restore point (supplementary) + explicit warning if System Restore is disabled.
- [x] `pnputil /add-driver /install`, device-state verification, structured operation log.

## Phase 3.5 — Authenticode/.cat verification (deferred)
- WinVerifyTrust-based certificate chain verification of downloaded packages, closing the gap noted in docs/ARCHITECTURE.md.

## Phase 4 — Rollback service
- Standalone rollback: reinstall snapshotted INF, re-enumerate device, log outcome.
- Exposed in UI as an explicit action tied to a specific past operation.

## Phase 5 — Index CI (can run in parallel with Phase 2-4)
- PR validation pipeline for the metadata-index repo: hash re-check, signature/publisher verify, optional VirusTotal check, auto-flag stale/broken source URLs on a schedule.

## v2 backlog (explicitly deferred)
- OEM `.exe` silent-install as a manual, explicitly-warned mode.
- WebView2 + React UI, only if WPF-UI's control set becomes a real limitation.
- OV/EV code-signing cert + winget listing.
