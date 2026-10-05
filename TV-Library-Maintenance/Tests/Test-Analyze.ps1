[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchPath)
# The harness creates disposable fixtures; the analyzer itself must not write.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $ScratchPath) { throw 'ScratchPath must be a new directory.' }
$null = New-Item -ItemType Directory -Path $ScratchPath
$ScratchPath = (Resolve-Path -LiteralPath $ScratchPath).Path
Import-Module (Join-Path $PSScriptRoot '..\TVLibraryMaintenance.psm1') -Force
function Assert($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
$root = Join-Path $ScratchPath 'Library'
$downloads = Join-Path $ScratchPath 'Downloads'
foreach ($folder in @($root,$downloads,(Join-Path $root 'Example (2020)'),(Join-Path $downloads 'Needs Review'))) { $null = New-Item -ItemType Directory -Path $folder -Force }
$homeData = Join-Path $ScratchPath 'TV_Home_Locations.csv'
$rows = @(
    [pscustomobject]@{ ShowName='Example (2020)'; LetterGroup='E'; HomeDrive='Y'; HomePath=(Join-Path $root 'Example (2020)'); Status='Assigned'; CurrentLocations=''; 'Size on disk (GiB)'='1.234' },
    [pscustomobject]@{ ShowName='Review Show'; LetterGroup='R'; HomeDrive='REVIEW'; HomePath=''; Status='Review required - same-path files differ'; CurrentLocations='a | b'; 'Size on disk (GiB)'='2.000' },
    [pscustomobject]@{ ShowName='Missing Folder'; LetterGroup='M'; HomeDrive='Y'; HomePath=(Join-Path $root 'Missing Folder'); Status='Assigned'; CurrentLocations=''; 'Size on disk (GiB)'='' },
    [pscustomobject]@{ ShowName='Outside'; LetterGroup='O'; HomeDrive='Y'; HomePath=(Join-Path $ScratchPath 'Other'); Status='Assigned'; CurrentLocations=''; 'Size on disk (GiB)'='0' }
)
$rows += $rows[0] | Select-Object *
$rows[-1].ShowName = 'Duplicate'
$duplicate = $rows[-1] | Select-Object *
$rows += $duplicate
$rows | Export-Csv -LiteralPath $homeData -NoTypeInformation
Set-Content -LiteralPath (Join-Path $root 'Example (2020)\Example.S01E01.mkv') -Value 'retained'
foreach ($name in @('Example.(2020).S01E01.mkv','Example.(2020).S01E02.1080p.mkv','Review.Show.S01E01.mkv','Missing.Folder.S01E01.mkv','Outside.S01E01.mkv','Duplicate.S01E01.mkv','Unknown.S01E01.mkv','nonsense.mkv','Example.(2020).S01E01E02.mkv')) {
    $path = Join-Path $downloads $name
    Set-Content -LiteralPath $path -Value 'download'
    (Get-Item -LiteralPath $path).LastWriteTime = (Get-Date).AddDays(-1)
}
Set-Content -LiteralPath (Join-Path $downloads 'Recent.S01E01.mkv') -Value 'recent'
Set-Content -LiteralPath (Join-Path $downloads 'Needs Review\Ignored.S01E01.mkv') -Value 'quarantine'
$config = [pscustomobject]@{ Mode='Analyze'; DownloadsPath=$downloads; QuarantinePath=(Join-Path $downloads 'Needs Review'); HomeMapPath=$homeData; StorageRoots=@([pscustomobject]@{Name='Local';Path=$root}); FileStabilityMinutes=10; MinimumFreeSpaceGB=0; RebalanceThresholdPercent=10 }
function Get-Snapshot {
    @(Get-ChildItem -LiteralPath $ScratchPath -Recurse -Force | ForEach-Object {
        $_.Refresh()
        '{0}|{1}|{2}|{3}' -f $_.FullName,$_.PSIsContainer,$_.LastWriteTimeUtc.Ticks,$(if (-not $_.PSIsContainer) { (Get-FileHash -LiteralPath $_.FullName).Hash } else { '' })
    }) -join "`n"
}
$before = Get-Snapshot
$report = Invoke-TVLibraryAnalysis -Config $config
$after = Get-Snapshot
if ($after -cne $before) { Compare-Object ($before -split [char]10) ($after -split [char]10) | ConvertTo-Json | Write-Host }
Assert ($after -ceq $before) 'Analyzer changed fixture files or directories.'
Assert ($report.HomeMap.Schema -eq 'TV_Home_Locations') 'Legacy schema not detected.'
Assert ($report.HomeMap.RowCount -eq 6) 'Rows were lost.'
Assert ($report.Downloads.Count -eq 10) 'Quarantine exclusion or download count failed.'
foreach ($status in @('DESTINATION_EPISODE_PRESENT','NOT_FOUND_IN_ASSIGNED_HOME','HOME_REVIEW','HOME_FOLDER_MISSING','HOME_INVALID','HOME_AMBIGUOUS','HOME_NOT_FOUND','UNMATCHED_EPISODE','EPISODE_REVIEW','RECENT_FILE')) {
    Assert (@($report.Downloads | Where-Object Status -eq $status).Count -eq 1) "Unexpected count for $status"
}
foreach ($mode in @('IntakeOnly','FullMaintenance')) {
    $config.Mode = $mode
    $blocked = $false
    try { Invoke-TVLibraryAnalysis -Config $config | Out-Null } catch { $blocked = $true }
    Assert $blocked "Write mode $mode was not blocked."
}
$config.Mode = 'Analyze'
Assert ((Get-Snapshot) -ceq $before) 'Rejected write mode changed fixtures.'
$normal = Join-Path $ScratchPath 'normalized.csv'
@([pscustomobject]@{ShowName='Normal';HomeRoot='Local';RelativePath='Normal'},[pscustomobject]@{ShowName='Escape';HomeRoot='Local';RelativePath='..\escape'}) | Export-Csv -LiteralPath $normal -NoTypeInformation
$normalized = Import-TVHomeMap -Path $normal -StorageRoots $config.StorageRoots
Assert ($normalized.Schema -eq 'NormalizedExample') 'Normalized example schema failed.'
Assert ($normalized.Entries[1].State -eq 'INVALID') 'Path traversal was accepted.'
$config.DownloadsPath = 'C:relative'
$blocked = $false
try { Invoke-TVLibraryAnalysis -Config $config | Out-Null } catch { $blocked = $true }
Assert $blocked 'Drive-relative DownloadsPath was accepted.'
$config.DownloadsPath = $downloads
$config.DownloadsPath = Join-Path $ScratchPath 'AbsentDownloads'
$blocked = $false
try { Invoke-TVLibraryAnalysis -Config $config | Out-Null } catch { $blocked = $true }
Assert $blocked 'Missing downloads silently produced an empty inventory.'
$config.DownloadsPath = $downloads
$empty = Join-Path $ScratchPath 'empty.csv'
Set-Content -LiteralPath $empty -Value 'ShowName,HomeRoot,RelativePath'
$blocked = $false
try { Import-TVHomeMap -Path $empty -StorageRoots $config.StorageRoots | Out-Null } catch { $blocked = $true }
Assert $blocked 'Empty CSV was accepted.'
# Production code must not contain filesystem writes or API refresh operations.
foreach ($file in @(Get-Item (Join-Path $PSScriptRoot '..\Invoke-TVLibraryMaintenance.ps1'), (Join-Path $PSScriptRoot '..\TVLibraryMaintenance.psm1'))) {
    $tokens=$null; $errors=$null
    $ast=[System.Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
    Assert ($errors.Count -eq 0) "Parser errors: $($file.Name)"
    $writes=@($ast.FindAll({param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -match '^(New-Item|Set-Content|Add-Content|Remove-Item|Move-Item|Rename-Item|Copy-Item|Export-Csv|Out-File|Invoke-RestMethod|Invoke-WebRequest)$'},$true))
    Assert ($writes.Count -eq 0) "Write command in production code: $($file.Name)"
}
'PASS: schema, assignments, title ambiguity, containment, episode outcomes, quarantine, rejected modes, and unchanged fixture snapshot.'
