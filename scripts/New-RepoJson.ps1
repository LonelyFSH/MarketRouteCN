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
    Punchline              = "国服批量比价、跨区路线分析与自动采购记录。"
    Description            = "面向 FF14 国服交易板的批量采购规划插件。V0.9 提供简洁工作区、多个采购清单、任意/HQ/NQ 条件、四大区完整报价、跨大区混合分析、完整挂单组合、路线优化、采购会话恢复，以及交易板购买后的自动完成记录。挂单来自 Universalis 众包接口，不是游戏运营方提供的实时市场数据。"
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
