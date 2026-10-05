# Local Analyze testing

## Setup wizard

Double-click `TVLibrarySetup.exe`. Select the Home-Server-Tools folder, confirm the Home CSV, browse to the downloads folder, and review Analyze settings. Start with one storage folder and use **+ Add another location** or the minus button to adjust the count. Optionally enable a weekly run, then select its weekday and local time. Select **Save settings**, then **Run Analyze**. A scheduled run requires the current Windows account to be signed in and shows results on screen; it does not save a report file. Setup saves `Config/config.json` and registers the `TV Library Maintenance - Analyze` Windows task when the option is enabled. Turning off a previously saved schedule and saving removes that task. If settings already exist, it asks before replacing them.

The wizard compiles with the Windows .NET Framework compiler:

```powershell
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /out:TVLibrarySetup.exe /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll .\TV-Library-Maintenance\Setup\TVLibrarySetup.cs
```

The tracked source builds locally; the branch workflow publishes a Windows executable artifact. Do not add a machine-specific `Config/config.json` to Git.

Keep all work on `development/tv-library-maintenance`. No write-mode test or merge is part of this milestone.

## Automated disposable fixtures

From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\TV-Library-Maintenance\Tests\Test-Analyze.ps1 -ScratchPath "$env:TEMP\TVAnalyze-$([guid]::NewGuid().ToString('N'))"
```

The test harness creates fixtures only in the explicitly supplied new ScratchPath. It leaves them available for inspection. The analyzer changes none of them. Tests check content hashes, file/directory inventory and refreshed modification times; legacy and sample schemas; review rows; duplicate titles; path containment; present/absent episodes; missing folders; quarantine exclusion; recent files; multi-episode review; and rejection of both write modes. Static checks parse the entry script and module and reject filesystem-write/API commands.

## Analyze with local configuration

After preparing Config/config.json, run:

```powershell
$report = & .\TV-Library-Maintenance\Invoke-TVLibraryMaintenance.ps1 -ConfigPath .\TV-Library-Maintenance\Config\config.json
$report.HomeSummary | Format-Table
$report.Summary | Format-Table
$report.Downloads | Format-Table Status, ShowName, EpisodeCode, HomePath
$report.Storage.Stats | Format-Table Name, Reachable, CapacityAvailable, UsedPercent, FreeGB, Error
```

Use a shell permitted to run scripts; a process-scoped execution-policy option does not change the machine's policy. No report is saved automatically. Import the module and call Invoke-TVLibraryAnalysis for integration without script exit behavior.

Errors reading downloads or the CSV fail the run (entry script exit 1). Assigned-library scan errors return LIBRARY_SCAN_FAILED for affected downloads. Unreachable roots and unknown capacity are explicit in Storage. A recent modification timestamp is RECENT_FILE, not evidence of actual file stability. NOT_FOUND_IN_ASSIGNED_HOME is a review observation, not permission to import.

Inspect exact title/year matches and all unresolved states before any later implementation of moves. Review-only rows must never gain destinations automatically.

Production validation in this milestone reads the actual CSV through Import-TVHomeMap only; it does not scan the full NAS/download library. Full production Analyze and upload-versus-live-CSV comparison remain manual checks.

