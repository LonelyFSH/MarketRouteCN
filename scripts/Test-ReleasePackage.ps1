[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ZipPath
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$resolved = (Resolve-Path $ZipPath).Path
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolved)

try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName })

    foreach ($required in @("MarketRouteCN.dll", "MarketRouteCN.json")) {
        if ($names -notcontains $required) {
            throw "Release package is missing required file: $required"
        }
    }

    $manifestEntry = $archive.Entries |
        Where-Object { $_.FullName -eq "MarketRouteCN.json" } |
        Select-Object -First 1

    $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
    try {
        $manifest = $reader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $reader.Dispose()
    }

    foreach ($requiredKey in @("Author", "Name", "InternalName", "AssemblyVersion", "DalamudApiLevel", "RepoUrl")) {
        if ($null -eq $manifest.$requiredKey -or [string]::IsNullOrWhiteSpace([string]$manifest.$requiredKey)) {
            throw "Distributed manifest is missing required generated field: $requiredKey"
        }
    }

    if ($manifest.InternalName -ne "MarketRouteCN") {
        throw "Unexpected InternalName: $($manifest.InternalName)"
    }

    if ([int]$manifest.DalamudApiLevel -ne 15) {
        throw "Unexpected DalamudApiLevel: $($manifest.DalamudApiLevel)"
    }

    Write-Host "Release package validation passed: $resolved"
}
finally {
    $archive.Dispose()
}
