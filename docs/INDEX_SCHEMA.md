# DriverLens Index Schema Specification

This document defines the JSON structure of the metadata-only driver index. 

This schema is a working draft for the real, signed GitHub metadata index in Phase 2. All updates to this schema should remain backward-compatible once Phase 2 ships.

## Schema Version
The root object specifies `schema_version` (integer), which indicates the parser compatibility level.
Current version: `1`

## Schema Structure

```json
{
  "schema_version": 1,
  "entries": [
    {
      "id": "contoso-net-3.2.1.0",
      "hwids": [
        "PCI\\VEN_1414&DEV_00B7&SUBSYS_00001414&REV_02", 
        "PCI\\VEN_1414&DEV_00B7"
      ],
      "compatible_ids": [
        "PCI\\VEN_1414&DEV_00B7&CC_020000"
      ],
      "provider": "Contoso",
      "version": "3.2.1.0",
      "release_date": "2026-04-10",
      "os": { 
        "min_build": 19041, 
        "arch": ["x64"] 
      },
      "source": {
        "url": "https://drivers.contoso.example/net/3.2.1.0.cab",
        "sha256": "3b1e9f4a2c7d0e8b5a6f1c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f",
        "authenticode_publisher": "Contoso Networking Inc"
      },
      "risk_level": "low",
      "known_good": true
    }
  ]
}
```

## Fields Definition

### Root Fields

| Field Name | Type | Description |
| :--- | :--- | :--- |
| `schema_version` | Integer | Version of the index format schema. |
| `entries` | Array | List of available driver candidates. |

---

### Candidate Entries (`entries[]`)

| Field Name | Type | Description |
| :--- | :--- | :--- |
| `id` | String | A unique identifier for the candidate entry. |
| `hwids` | Array of Strings | Case-insensitive hardware identifiers. These are OR-matched against the device's own Hardware IDs. |
| `compatible_ids` | Array of Strings | Case-insensitive compatible device identifiers. Used as a fallback if no exact `hwid` match is found. |
| `provider` | String | The driver provider name (e.g. "Intel", "Realtek"). |
| `version` | String | Dotted-decimal version string (e.g., "10.0.19041.1"). |
| `release_date` | String (ISO 8601) | The release date of the driver in `YYYY-MM-DD` format. |
| `os` | Object | Target OS compatibility parameters. |
| `source` | Object | Package source details. |
| `risk_level` | String (`low`, `medium`, `high`) | Level of regression or safety risk associated with this driver. |
| `known_good` | Boolean | True if the version is verified stable. A `known_good: false` candidate is deprioritized during ranking. |

---

### OS Requirements (`os`)

| Field Name | Type | Description |
| :--- | :--- | :--- |
| `min_build` | Integer | Minimum Windows build number required (e.g., `19041` for Windows 10 20H1). |
| `arch` | Array of Strings | Supported CPU architectures (e.g., `["x64", "x86", "arm64"]`). |

---

### Source Information (`source`)

| Field Name | Type | Description |
| :--- | :--- | :--- |
| `url` | String | Direct download URL to the driver archive (usually `.cab` or `.exe`). |
| `sha256` | String | Hex-encoded SHA-256 hash of the download package. |
| `authenticode_publisher` | String? | Common name of the digital signature certificate publisher. Set to `null` if the driver candidate is unsigned. |

## Matching Logic
- **Or-Matching**: An entry's `hwids` or `compatible_ids` array matches a device if any element in the array matches any element in the corresponding device's hardware or compatible ID lists.
- **Unsigned Drivers**: Candidates with `authenticode_publisher: null` are skipped during matching in v1 for safety.
