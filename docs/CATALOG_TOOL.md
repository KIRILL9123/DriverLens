# DriverLens.CatalogSearch

`DriverLens.CatalogSearch` is a developer-only CLI tool that automates searching the Microsoft Update Catalog, resolving download links, downloading packages, extracting content, verifying target Hardware IDs, and generating candidate entries for `index/shards/net.json`.

It replaces the manual web browser and PowerShell download/extract scripting workflows.

## Usage

### 1. Search the Microsoft Update Catalog
To search for a driver package in the catalog:

```bash
dotnet run --project tools/DriverLens.CatalogSearch -- search "<query>"
```

Example:
```bash
dotnet run --project tools/DriverLens.CatalogSearch -- search "Realtek High Definition Audio"
```

This returns a numbered list of updates including their title, version, products, release date, package size, and Update GUID.

### 2. Resolve, Verify, and Generate Template
To resolve the direct download URL, download the package, verify that it actually supports a specific Hardware ID, check the Authenticode signature, and print a template for `net.json`:

```bash
dotnet run --project tools/DriverLens.CatalogSearch -- resolve <guid> --expect-hwid "<HWID>" --version "<version>" --release-date "<yyyy-MM-dd>"
```

Example:
```bash
dotnet run --project tools/DriverLens.CatalogSearch -- resolve 7cd3e302-9a0f-453b-acee-01890923dd97 --expect-hwid "HDAUDIO\FUNC_01&VEN_10EC&DEV_0897" --version "6.0.9927.1" --release-date "2025-12-15"
```

If the `--version` or `--release-date` parameters are omitted, the tool will still perform the full verification (download, extraction, HWID checks, and signature checks) but will refuse to print the JSON template block.

## Technical Notes

### Screen-Scraping / Page Parsing
This tool uses screen-scraping of undocumented Microsoft Update Catalog web interfaces (specifically `Search.aspx` and `DownloadDialog.aspx`). 
* **Fragility warning:** Because these pages are meant for human web browsers and do not offer a stable public REST API, Microsoft can change the HTML structure, table cell IDs, query formats, or form post payloads at any time without notice. If that happens, the scraper will fail, and the parser code in this tool will need to be updated.
* **Intended Use:** This is a developer convenience tool, not a production dependency. It is explicitly not referenced by, or wired into, `DriverLens.App`.

### Verification and Safety
* **HWID Substring Verification:** The `resolve` command extracts all `.inf` files in the `.cab` package and checks for the existence of the expected Hardware ID string. If the ID is not found in any of the `.inf` files, the tool stops and prints a warning.
* **Manual Review Required:** The generated JSON block printed to the standard output must always be manually inspected, modified (e.g. to add sibling HWIDs or verify catalog version info), and placed into `index/shards/net.json` by a developer.
* **Signing:** The tool does NOT write directly to the index and does NOT sign index files. Any changes to the index must still be signed using `DriverLens.IndexSigner`.
