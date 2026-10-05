# TV Library Maintenance

Development milestone: graphical setup wizard plus read-only Analyze for Windows PowerShell 5.1 and PowerShell 7.

Run `TVLibrarySetup.exe`. Browse for the Home-Server-Tools folder, Home CSV, and downloads folder. Start with one storage folder; click **+ Add another location** for each additional folder, and use the minus button to remove one. Choose **Run Analyze automatically once a week** to select one day and time. Click **Save settings**, then **Run Analyze** to see results in the window. Scheduled runs appear on screen while you are signed in to Windows. No JSON editing or PowerShell command is needed.

The setup source is `Setup/TVLibrarySetup.cs`; it builds as a Windows desktop executable against the Windows .NET Framework included on Windows. The GitHub Actions workflow builds the executable and publishes it as a workflow artifact.

The entry script loads a reusable module and returns a structured report. It reads configuration, the Home CSV, downloads, assigned show folders, and volume capacity. It creates no runtime directory, lock, log, report file, destination, or quarantine folder. It deletes nothing and makes no Sonarr/API calls. IntakeOnly and FullMaintenance fail before scanning.

The existing `TV_Home_Locations.csv` format is supported directly; no CSV migration is needed. HomePath is authoritative. HomeDrive remains metadata. Review rows and duplicate normalized titles are reported without selecting destinations.

See [configuration](Docs/CONFIGURATION.md) and [local testing](Docs/TESTING.md).

The module exports `Invoke-TVLibraryAnalysis` and `Import-TVHomeMap`. A broader PC-tools project can call these functions without launching the entry script. Configuration defaults are relative to this subtool, independent of the caller's working directory.

Analyze reports observations, not approved move plans. Exact title matching retains years. Files absent from the assigned Home folder are only review candidates; alternate roots, alternate names, and multi-episode coverage are not verified. Rebalancing is a volume utilization observation only; no show selection or Home updates occur.

