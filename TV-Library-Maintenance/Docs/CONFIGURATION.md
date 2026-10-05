# Configuration and Home map

Create a local `Config/config.json` from `config.example.json` and adjust DownloadsPath. This setup step writes your configuration; running Analyze does not. Local configuration is ignored by Git.

The Windows setup wizard can create or update this file. It loads saved values when present and also repairs older settings files whose Windows path backslashes were not escaped as valid JSON. It writes a proper replacement only when you select **Save settings**.

Analyze requires Mode, DownloadsPath, HomeMapPath, StorageRoots, FileStabilityMinutes, MinimumFreeSpaceGB, and RebalanceThresholdPercent. QuarantinePath is optional; files already below it are excluded. All source and root paths must be absolute. Root names must be unique. At least one root is allowed for local tests. Only Mode=Analyze is accepted.

## Detected production schema

The existing TV Home locations CSV format is supported directly. Choose your own CSV file in the setup wizard.

| Column | Interpretation |
| --- | --- |
| ShowName | Canonical title; required for matching |
| LetterGroup | Preserved metadata |
| HomeDrive | Preserved drive label (Y, V, Q, or REVIEW); never translated to a destination |
| HomePath | Absolute assigned show folder; must be below exactly one configured root |
| Status | Only Assigned is eligible; other statuses remain review-only |
| CurrentLocations | Preserved verbatim, including pipe-separated review paths; never used as a fallback destination |
| Size on disk (GiB) | Preserved numeric text, not recalculated or used as available space |

Observed: 776 rows, 772 Assigned and 4 marked `Review required - same-path files differ`. Drive labels: Y=588, V=106, Q=78, REVIEW=4. Extra CSV columns are retained in each entry's Original object. No original values are rewritten.

The older sample schema `ShowName,HomeRoot,RelativePath` remains supported. RelativePath must stay below its named root; rooted paths and dot traversal are invalid. The example is a fixture, not a replacement for the existing Home file.

The setup wizard starts with one storage folder. Add or remove folders as needed; each configured HomePath must be below exactly one of them. An optional weekly schedule runs on the selected weekday and local time while the Windows account is signed in. The scheduled Analyze results appear on screen and are not saved to a report file. Keep the selected tool folder in place. The schedule is saved in Config/config.json and as the Windows task `TV Library Maintenance - Analyze`; turning off a previously saved schedule and saving removes that task.

Volume capacity can be unavailable on UNC shares. Analyze reports that explicitly and excludes unknown values from utilization comparisons. Shared roots may use the same physical volume; capacity observations do not establish independent disks.

