# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
