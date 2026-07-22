[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string] $Version,

    [Parameter(Mandatory = $false)]
    [string] $Tag = "v$Version",

    [Parameter(Mandatory = $false)]
    [string] $OutputPath = "repo.json",

    [Parameter(Mandatory = $false)]
    [string] $Changelog = "MarketRoute CN $Version"
)

$ErrorActionPreference = "Stop"

$owner = "LonelyFSH"
$repository = "MarketRouteCN"
$assetName = "MarketRouteCN.zip"
$releaseUrl = "https://github.com/$owner/$repository/releases/download/$Tag/$assetName"

$entry = [ordered]@{
    Author                 = "LonelyFSH"
    Name                   = "MarketRoute CN"
    Punchline              = "国服全品类批量比价与跨服采购路线规划。"
    Description            = "面向 FF14 国服交易板的批量采购规划插件。支持自动检索物品、数量与任意/HQ/NQ 品质要求、国服四大区完整报价比较、大区内按服务器分组采购路线，以及查询时间和市场数据时间显示。价格来自 Universalis 众包数据。"
    Tags                   = @("market", "china", "shopping", "price")
    CategoryTags           = @("Utility", "UI")
    InternalName           = "MarketRouteCN"
    AssemblyVersion        = $Version
    TestingAssemblyVersion = $null
    RepoUrl                = "https://github.com/$owner/$repository"
    ApplicableVersion      = "any"
    DalamudApiLevel        = 15
    IsHide                 = $false
    IsTestingExclusive     = $false
    DownloadLinkInstall    = $releaseUrl
    DownloadLinkUpdate     = $releaseUrl
    LastUpdate             = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    Changelog              = $Changelog
}

$payload = @($entry)
$json = ConvertTo-Json -InputObject $payload -Depth 10

$fullOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$directory = [System.IO.Path]::GetDirectoryName($fullOutputPath)
if (-not [string]::IsNullOrWhiteSpace($directory)) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($fullOutputPath, $json + [Environment]::NewLine, $utf8NoBom)

Write-Host "Generated custom repository manifest:"
Write-Host "  Version: $Version"
Write-Host "  Tag:     $Tag"
Write-Host "  Asset:   $releaseUrl"
Write-Host "  Output:  $fullOutputPath"
