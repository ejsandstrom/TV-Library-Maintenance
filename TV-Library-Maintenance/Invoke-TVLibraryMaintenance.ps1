[CmdletBinding()]
param([string]$ConfigPath = (Join-Path $PSScriptRoot 'Config\config.json'))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try {
    Import-Module (Join-Path $PSScriptRoot 'TVLibraryMaintenance.psm1') -Force
    $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    Invoke-TVLibraryAnalysis -Config $config
}
catch {
    Write-Error $_.Exception.Message -ErrorAction Continue
    exit 1
}

