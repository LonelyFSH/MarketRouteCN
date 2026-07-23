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
    Punchline              = "国服全品类批量比价、智能路线与连续采购工作区。"
    Description            = "面向 FF14 国服交易板的批量采购规划插件。V0.8 提供概览工作区与直接跳转流程，支持多个采购清单、文本 CSV JSON 导入导出、任意/HQ/NQ 条件、四大区完整报价比较、完整挂单组合、最低价/平衡/最少服务器路线策略、报价趋势与风险价、目标总价、采购会话与中断恢复，并显示查询时间和市场数据时间。价格来自 Universalis 众包数据。"
    Tags                   = @("market", "china", "shopping", "price", "route")
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
