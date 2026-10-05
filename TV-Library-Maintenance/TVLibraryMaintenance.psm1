Set-StrictMode -Version Latest

function Get-TVTitleKey {
    param([string]$Name)
    return (($Name -replace '[._]+', ' ' -replace '\s+', ' ').Trim().ToLowerInvariant())
}

function Test-TVAbsolutePath {
    param([string]$Path)
    return ($Path -match '^[A-Za-z]:[\\/]' -or $Path -match '^\\\\[^\\/]+\\[^\\/]+(?:\\|$)')
}

function Test-TVPathWithinRoot {
    param([string]$Path, [string]$Root)
    # Compare absolute paths on a segment boundary; never accept traversal.
    if ($Path -match '(^|[\\/])\.\.?([\\/]|$)' -or $Root -match '(^|[\\/])\.\.?([\\/]|$)') { return $false }
    if (-not (Test-TVAbsolutePath $Path) -or -not (Test-TVAbsolutePath $Root)) { return $false }
    $rootPrefix = $Root.Replace('/', '\').TrimEnd('\') + '\'
    return $Path.Replace('/', '\').StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-TVAnalysisConfig {
    param($Config)
    foreach ($name in @('Mode','DownloadsPath','HomeMapPath','StorageRoots','FileStabilityMinutes','MinimumFreeSpaceGB','RebalanceThresholdPercent')) {
        if ($null -eq $Config.PSObject.Properties[$name]) { throw "Missing configuration value: $name" }
    }
    if ($Config.Mode -ne 'Analyze') { throw 'Only Analyze mode is supported. Write modes are disabled.' }
    foreach ($name in @('DownloadsPath','HomeMapPath')) {
        if ([string]::IsNullOrWhiteSpace([string]$Config.$name) -or -not (Test-TVAbsolutePath ([string]$Config.$name))) { throw "$name must be an absolute path." }
    }
    if (@($Config.StorageRoots).Count -eq 0) { throw 'At least one StorageRoot is required.' }
    $names = @{}
    foreach ($root in $Config.StorageRoots) {
        if ($null -eq $root.PSObject.Properties['Name'] -or $null -eq $root.PSObject.Properties['Path']) { throw 'Each StorageRoot requires Name and Path.' }
        if ([string]::IsNullOrWhiteSpace($root.Name) -or $names.ContainsKey([string]$root.Name)) { throw 'StorageRoot names must be nonempty and unique.' }
        $names[[string]$root.Name] = $true
        if (-not (Test-TVAbsolutePath ([string]$root.Path)) -or $root.Path -match '(^|[\\/])\.\.?([\\/]|$)') { throw 'StorageRoot paths must be absolute without dot traversal.' }
    }
    foreach ($name in @('FileStabilityMinutes','MinimumFreeSpaceGB','RebalanceThresholdPercent')) {
        $number = 0.0
        if (-not [double]::TryParse([string]$Config.$name, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -or [double]::IsNaN($number) -or [double]::IsInfinity($number) -or $number -lt 0) { throw "$name must be a finite nonnegative number." }
    }
}

function Import-TVHomeMap {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)]$StorageRoots)
    $rows = @(Import-Csv -LiteralPath $Path -ErrorAction Stop)
    if ($rows.Count -eq 0) { throw 'Home map contains no data rows; its schema cannot be confirmed.' }
    $columns = @($rows[0].PSObject.Properties.Name)
    $legacyColumns = @('ShowName','LetterGroup','HomeDrive','HomePath','Status','CurrentLocations','Size on disk (GiB)')
    $normalizedColumns = @('ShowName','HomeRoot','RelativePath')
    if (@($legacyColumns | Where-Object { $_ -notin $columns }).Count -eq 0) { $schema = 'TV_Home_Locations' }
    elseif (@($normalizedColumns | Where-Object { $_ -notin $columns }).Count -eq 0) { $schema = 'NormalizedExample' }
    else { throw ('Unsupported Home schema. Detected columns: ' + ($columns -join ', ')) }
    $entries = @(foreach ($row in $rows) {
        $homePath = $null
        $homeRoot = $null
        $state = 'ASSIGNED'
        $reason = $null
        if ([string]::IsNullOrWhiteSpace([string]$row.ShowName)) { $state = 'INVALID'; $reason = 'Empty ShowName.' }
        if ($schema -eq 'TV_Home_Locations') {
            $homePath = [string]$row.HomePath
            if ($row.Status.Trim() -ne 'Assigned' -or $row.HomeDrive.Trim() -eq 'REVIEW') {
                $state = 'REVIEW'; $reason = [string]$row.Status
            }
            elseif ([string]::IsNullOrWhiteSpace($homePath)) { $state = 'INVALID'; $reason = 'Assigned row has no HomePath.' }
            else {
                # HomeDrive is metadata: HomePath is authoritative, never a drive-letter translation.
                $roots = @($StorageRoots | Where-Object { Test-TVPathWithinRoot -Path $homePath -Root $_.Path })
                if ($roots.Count -ne 1) { $state = 'INVALID'; $reason = 'HomePath must be below exactly one configured root.' }
                else { $homeRoot = [string]$roots[0].Name }
            }
        }
        else {
            $roots = @($StorageRoots | Where-Object { $_.Name -eq $row.HomeRoot })
            if ($roots.Count -ne 1 -or [string]::IsNullOrWhiteSpace($row.RelativePath) -or [IO.Path]::IsPathRooted($row.RelativePath)) {
                $state = 'INVALID'; $reason = 'Invalid HomeRoot or RelativePath.'
            }
            else {
                $homePath = Join-Path $roots[0].Path $row.RelativePath
                $homeRoot = [string]$roots[0].Name
                if (-not (Test-TVPathWithinRoot -Path $homePath -Root $roots[0].Path)) { $state = 'INVALID'; $reason = 'RelativePath escapes the configured root.' }
            }
        }
        if ([string]::IsNullOrWhiteSpace([string]$row.ShowName)) { $state = 'INVALID'; $reason = 'Empty ShowName.' }
        [pscustomobject]@{ ShowName = [string]$row.ShowName; Key = Get-TVTitleKey $row.ShowName; State = $state; Reason = $reason; HomeRoot = $homeRoot; HomePath = $homePath; Original = $row }
    })
    [pscustomobject]@{ Schema = $schema; Columns = $columns; Entries = $entries; RowCount = $rows.Count }
}

function Get-TVDownloadAnalysis {
    [CmdletBinding()]
    param($Config, $HomeMap)
    $index = @{}
    foreach ($entry in $HomeMap.Entries) {
        if (-not $index.ContainsKey($entry.Key)) { $index[$entry.Key] = @() }
        $index[$entry.Key] += $entry
    }
    # Read failures terminate the scan; partial inventories must never imply missing episodes.
    $files = @(Get-ChildItem -LiteralPath $Config.DownloadsPath -File -Recurse -ErrorAction Stop |
        Where-Object { $_.Extension.ToLowerInvariant() -in @('.mkv','.mp4','.avi','.m4v','.mov','.ts','.wmv') })
    $cutoff = (Get-Date).AddMinutes(-[double]$Config.FileStabilityMinutes)
    $inventory = @{}
    foreach ($file in $files) {
        if ($null -ne $Config.PSObject.Properties['QuarantinePath'] -and $Config.QuarantinePath) {
            if (Test-TVPathWithinRoot -Path $file.FullName -Root $Config.QuarantinePath) { continue }
        }
        $result = [ordered]@{ Status = 'UNMATCHED_EPISODE'; ShowName = $null; EpisodeCode = $null; SourcePath = $file.FullName; SizeBytes = $file.Length; HomePath = $null; Detail = $null }
        if ($file.LastWriteTime -gt $cutoff) { $result.Status = 'RECENT_FILE'; $result.Detail = 'Modification time is recent; stability has not been verified.' }
        else {
            $identity = ConvertTo-EpisodeIdentity -File $file
            if ($null -ne $identity) {
                $result.ShowName = $identity.ShowName
                $result.EpisodeCode = $identity.EpisodeCode
                # Multi-episode, ranges, and suffixes need review, never single-episode inference.
                if ($file.BaseName -match '(?i)S\d{1,2}[ ._-]?E\d{1,3}(?:E\d|[ ._-]+E\d|[-_]\d{1,3}(?!\d)|[a-z])') {
                    $result.Status = 'EPISODE_REVIEW'
                }
                else {
                    $key = Get-TVTitleKey $identity.ShowName
                    if (-not $index.ContainsKey($key)) { $result.Status = 'HOME_NOT_FOUND'; $result.Detail = 'Exact normalized title required, including year when present.' }
                    elseif ($index[$key].Count -ne 1) { $result.Status = 'HOME_AMBIGUOUS' }
                    else {
                        $homeData = $index[$key][0]
                        $result.ShowName = $homeData.ShowName
                        if ($homeData.State -ne 'ASSIGNED') { $result.Status = 'HOME_' + $homeData.State; $result.Detail = $homeData.Reason }
                        else {
                            $result.HomePath = $homeData.HomePath
                            if (-not $inventory.ContainsKey($homeData.HomePath)) {
                                try {
                                    if (-not (Test-Path -LiteralPath $homeData.HomePath -PathType Container -ErrorAction Stop)) {
                                        $inventory[$homeData.HomePath] = [pscustomobject]@{ Status = 'HOME_FOLDER_MISSING'; Codes = @(); Detail = 'Assigned folder is absent; no destination created.' }
                                    }
                                    else {
                                        $codes = @(Get-ChildItem -LiteralPath $homeData.HomePath -File -Recurse -ErrorAction Stop |
                                            Where-Object { $_.Extension.ToLowerInvariant() -in @('.mkv','.mp4','.avi','.m4v','.mov','.ts','.wmv') } |
                                            ForEach-Object {
                                                $m = [regex]::Match($_.BaseName, '(?i)S(?<s>\d{1,2})[ ._-]?E(?<e>\d{1,3})')
                                                if ($m.Success) { 'S{0:D2}E{1:D2}' -f [int]$m.Groups['s'].Value, [int]$m.Groups['e'].Value }
                                            })
                                        $inventory[$homeData.HomePath] = [pscustomobject]@{ Status = 'SCANNED'; Codes = $codes; Detail = $null }
                                    }
                                }
                                catch { $inventory[$homeData.HomePath] = [pscustomobject]@{ Status = 'LIBRARY_SCAN_FAILED'; Codes = @(); Detail = $_.Exception.Message } }
                            }
                            $scan = $inventory[$homeData.HomePath]
                            if ($scan.Status -ne 'SCANNED') { $result.Status = $scan.Status; $result.Detail = $scan.Detail }
                            else {
                                $m = [regex]::Match($identity.EpisodeCode, 'S(?<s>\d+)E(?<e>\d+)')
                                $code = 'S{0:D2}E{1:D2}' -f [int]$m.Groups['s'].Value, [int]$m.Groups['e'].Value
                                if ($code -in $scan.Codes) { $result.Status = 'DESTINATION_EPISODE_PRESENT' }
                                else { $result.Status = 'NOT_FOUND_IN_ASSIGNED_HOME'; $result.Detail = 'Review candidate only. Other roots, alternate naming, and multi-episode coverage are not verified.' }
                            }
                        }
                    }
                }
            }
        }
        [pscustomobject]$result
    }
}

function Invoke-TVLibraryAnalysis {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Config)
    Assert-TVAnalysisConfig -Config $Config
    $homeData = Import-TVHomeMap -Path $Config.HomeMapPath -StorageRoots $Config.StorageRoots
    $storage = Get-StorageAnalysis -Config $Config
    $downloads = @(Get-TVDownloadAnalysis -Config $Config -HomeMap $homeData)
    [pscustomobject]@{
        Mode = 'Analyze'
        HomeMap = $homeData
        Storage = $storage
        Downloads = $downloads
        Summary = @($downloads | Group-Object Status | Select-Object Name, Count)
        HomeSummary = @($homeData.Entries | Group-Object State | Select-Object Name, Count)
        Limitations = 'No write actions or automatic destination decisions. Capacity is reported by Windows for each selected folder or network share. File age does not prove stability.'
    }
}

Export-ModuleMember -Function Invoke-TVLibraryAnalysis, Import-TVHomeMap

function Get-StorageRootStat {
    param([Parameter(Mandatory)]$Root)
    $path = [string]$Root.Path
    $stat = [pscustomobject]@{ Name=[string]$Root.Name; Path=$path; Reachable=$false; CapacityAvailable=$false; UsedBytes=$null; FreeBytes=$null; TotalBytes=$null; UsedPercent=$null; FreeGB=$null; Error=$null }
    try {
        $stat.Reachable = Test-Path -LiteralPath $path -PathType Container -ErrorAction Stop
        if ($stat.Reachable) {
            if (-not ('TVLibraryMaintenance.NativeDiskSpace' -as [type])) {
                Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace TVLibraryMaintenance {
    public static class NativeDiskSpace {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetDiskFreeSpaceEx(string directoryName, out ulong freeBytesAvailable, out ulong totalNumberOfBytes, out ulong totalNumberOfFreeBytes);
        public static ulong[] GetCapacity(string directoryName) {
            ulong available, total, free;
            if (!GetDiskFreeSpaceEx(directoryName, out available, out total, out free))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return new ulong[] { available, total, free };
        }
    }
}
"@ -ErrorAction Stop
            }
            $capacity = [TVLibraryMaintenance.NativeDiskSpace]::GetCapacity($path)
            $stat.TotalBytes = [long]$capacity[1]
            $stat.FreeBytes = [long]$capacity[0]
            $stat.UsedBytes = $stat.TotalBytes - [long]$capacity[2]
            if ($stat.TotalBytes -le 0) { throw 'Capacity has no positive total.' }
            $stat.UsedPercent = [math]::Round(100.0 * $stat.UsedBytes / $stat.TotalBytes, 2)
            $stat.FreeGB = [math]::Round($stat.FreeBytes / 1GB, 2)
            $stat.CapacityAvailable = $true
        }
    }
    catch { $stat.Error = $_.Exception.Message }
    return $stat
}
function Get-StorageAnalysis {
    param($Config)

    $stats = @(foreach ($root in $Config.StorageRoots) {
        Get-StorageRootStat -Root $root
    })

    foreach ($stat in $stats) {
        if (-not $stat.Reachable) {
            Write-Warning "Storage root is not reachable: $($stat.Name) [$($stat.Path)]"
            continue
        }

        if (-not $stat.CapacityAvailable) {
            Write-Warning "Storage capacity unavailable for $($stat.Name): $($stat.Error)"
            continue
        }

        Write-Verbose ("Storage {0}: {1}% used, {2:N2} GB free [{3}]" -f $stat.Name, $stat.UsedPercent, $stat.FreeGB, $stat.Path)

        if ($stat.FreeGB -lt [double]$Config.MinimumFreeSpaceGB) {
            Write-Warning ("Storage {0} is below the minimum free-space floor of {1} GB." -f $stat.Name, $Config.MinimumFreeSpaceGB)
        }
    }

    $reachable = @($stats | Where-Object { $_.Reachable -and $_.CapacityAvailable })
    if ($reachable.Count -ge 2) {
        $maxUsed = ($reachable | Measure-Object -Property UsedPercent -Maximum).Maximum
        $minUsed = ($reachable | Measure-Object -Property UsedPercent -Minimum).Minimum
        $spread = [math]::Round(([double]$maxUsed - [double]$minUsed), 2)

        $needsRebalance = $spread -gt [double]$Config.RebalanceThresholdPercent
        Write-Verbose ("Utilization spread: {0:N2} percentage points. Threshold: {1:N2}. Rebalance needed: {2}" -f $spread, [double]$Config.RebalanceThresholdPercent, $needsRebalance)

        return [pscustomobject]@{
            Stats = $stats
            SpreadPercent = $spread
            RebalanceNeeded = $needsRebalance
        }
    }

    return [pscustomobject]@{
        Stats = $stats
        SpreadPercent = $null
        RebalanceNeeded = $false
    }
}

function ConvertTo-EpisodeIdentity {
    param([System.IO.FileInfo]$File)

    $match = [regex]::Match($File.BaseName, '(?i)(?<season>S\d{1,2})[ ._-]?(?<episode>E\d{1,3}(?:E\d{1,3})*)')
    if (-not $match.Success) {
        return $null
    }

    $prefix = $File.BaseName.Substring(0, $match.Index)
    $show = ($prefix -replace '[._]+',' ' -replace '\s+',' ').Trim(' ','-')

    if ([string]::IsNullOrWhiteSpace($show)) {
        return $null
    }

    return [pscustomobject]@{
        ShowName = $show
        EpisodeCode = ($match.Groups['season'].Value + $match.Groups['episode'].Value).ToUpperInvariant()
        SourcePath = $File.FullName
        SizeBytes = $File.Length
        LastWriteTime = $File.LastWriteTime
    }
}



