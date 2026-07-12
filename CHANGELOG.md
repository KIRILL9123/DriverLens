# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.0] - 2026-07-12
### Added
- Multi-step driver install pipeline implementation (`DriverInstallService` in `DriverLens.Install`).
- Streaming package downloader with on-the-fly SHA-256 validation (`HttpPackageDownloader`).
- CAB extraction utility using Windows `expand.exe` with single INF requirement constraint (`ExpandCabExtractor`).
- Snapshot driver backup service using `pnputil /export-driver` and JSON metadata records (`PnpUtilSnapshotService`).
- System Restore Point WMI integration and registry heuristic state check (`WmiRestorePointService`).
- JSON Lines operation logging database (`%LOCALAPPDATA%\DriverLens\logs\operations.jsonl`).
- Published INF path capture support in SetupAPI device scanner.
- WPF client updates: "Обновить" row actions, step progress reporting, ContentDialog confirmations, and warning notifications.
- Unit tests for downloader hashes, snapshot record structures, and operation log files.

## [0.2.0] - 2026-07-12
### Added
- Offline index signing tool (`tools/DriverLens.IndexSigner`).
- ECDSA P-256 (SHA-256) signature verification client logic (`SignedIndexVerifier`).
- SQLite-based database cache store (`LocalCacheStore` in `DriverLens.Data`).
- Background GitHub repository index synchronization service (`GithubIndexSyncService` with ETag handling and signature validation).
- Cache-first startup path in WPF client with background synchronization and UI sync status indicator.
- Unit test coverage for signature verification and background synchronization with HTTP mocks.
- Signing and verification process documentation (`docs/SIGNING.md`).

## [0.1.0] - 2026-07-11
### Added
- SetupAPI-based device scanner (`DriverLens.Scanner` library project).
- Deterministic 6-tier matching engine (`RankedMatchingEngine` in `DriverLens.Core`).
- JSON fixture-based index repository (`JsonFixtureIndexRepository`).
- Read-only WPF-UI device list grouped by device class, with Current → Proposed detail using WPF-UI controls and custom Mica theme.
- System restore point creation API (stubbed P/Invoke hooks).
- Unit test suite for `RankedMatchingEngine` with 100% specification coverage.
- Detailed metadata-index schema documentation (`docs/INDEX_SCHEMA.md`).

## [0.0.1] - 2026-07-11
### Added
- Five-project .NET 8 solution scaffold.
- WPF-UI shell with MVVM composition root, UAC manifest.
- Architecture documentation (`docs/ARCHITECTURE.md`) and initial roadmap (`docs/ROADMAP.md`).
