param(
    [string]$Edition = "",
    [string]$Version = "",
    [string]$Config = "",
    [string[]]$Ads = @(),
    [string]$AdsFolder = "",
    [Nullable[bool]]$AdsTab = $null,
    [Nullable[bool]]$AdsSettings = $null,
    [Nullable[bool]]$Advanced = $null,
    [Nullable[bool]]$AllAdFolders = $null,
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "TomatoFlow.csproj"
$videoExtensions = @(".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".mpg", ".mpeg")

$configData = $null
if ($Config) {
    $configPath = if (Test-Path -LiteralPath $Config) { (Resolve-Path -LiteralPath $Config).Path } else { Join-Path $root $Config }
    if (-not (Test-Path -LiteralPath $configPath)) {
        throw "Config file not found: $configPath"
    }

    $configData = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json
}

if (-not $Edition -and $configData -and $configData.edition) {
    $Edition = [string]$configData.edition
}

if (-not $Version -and $configData -and $configData.version) {
    $Version = [string]$configData.version
}

if (-not $Edition) {
    $Edition = "Consumer"
}

if (-not $Version) {
    $Version = "1.1.0"
}

$validEditions = @("Consumer", "Normal", "Developer")
if ($validEditions -notcontains $Edition) {
    throw "Edition must be Consumer, Normal, or Developer (got '$Edition')."
}

if ($null -eq $AdsTab) {
    if ($configData -and $configData.features -and $null -ne $configData.features.adsTab) {
        $AdsTab = [bool]$configData.features.adsTab
    }
    else {
        $AdsTab = $Edition -ne "Consumer"
    }
}

if ($null -eq $AdsSettings) {
    if ($configData -and $configData.features -and $null -ne $configData.features.adsSettings) {
        $AdsSettings = [bool]$configData.features.adsSettings
    }
    else {
        $AdsSettings = $Edition -ne "Consumer"
    }
}

if ($null -eq $Advanced) {
    if ($configData -and $configData.features -and $null -ne $configData.features.advanced) {
        $Advanced = [bool]$configData.features.advanced
    }
    else {
        $Advanced = $Edition -eq "Developer"
    }
}

if ($null -eq $AllAdFolders) {
    if ($configData -and $configData.features -and $null -ne $configData.features.allAdFolders) {
        $AllAdFolders = [bool]$configData.features.allAdFolders
    }
    else {
        $AllAdFolders = $Edition -ne "Consumer"
    }
}

$adList = New-Object System.Collections.Generic.List[string]
$seenAds = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::OrdinalIgnoreCase)

if ($configData -and $configData.ads) {
    foreach ($item in $configData.ads) {
        if ($item -and $seenAds.Add([string]$item)) {
            $adList.Add([string]$item)
        }
    }
}

foreach ($item in $Ads) {
    if ($item -and $seenAds.Add([string]$item)) {
        $adList.Add([string]$item)
    }
}

if ($AdsFolder) {
    if (-not (Test-Path -LiteralPath $AdsFolder)) {
        throw "Ads folder not found: $AdsFolder"
    }

    $found = Get-ChildItem -LiteralPath $AdsFolder -Recurse -File | Where-Object {
        $videoExtensions -contains $_.Extension.ToLowerInvariant()
    }

    foreach ($file in $found) {
        if ($seenAds.Add($file.FullName)) {
            $adList.Add($file.FullName)
        }
    }
}

$missingAds = @($adList | Where-Object { -not (Test-Path -LiteralPath $_) })
if ($missingAds.Count -gt 0) {
    throw "These selected ad files do not exist: $($missingAds -join ', ')"
}

$publish = Join-Path $root (Join-Path "publish" $Edition)
$release = Join-Path $root "release"
$adsTarget = Join-Path $publish "Assets\Ads"

function Stop-BuildProcesses {
    param([string]$Folder)

    $prefix = [System.IO.Path]::GetFullPath($Folder).TrimEnd("\") + "\"
    Get-Process -Name "TomatoFlow" -ErrorAction SilentlyContinue | ForEach-Object {
        $processPath = $null
        try { $processPath = $_.Path } catch { }
        if ($processPath -and $processPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-Host "==> Closing running build $processPath"
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep -Milliseconds 400
}

Write-Host ""
Write-Host "Tomato Flow build"
Write-Host "  edition      $Edition"
Write-Host "  version      $Version"
Write-Host "  ads tab      $AdsTab"
Write-Host "  ad settings  $AdsSettings"
Write-Host "  advanced     $Advanced"
Write-Host "  all folders  $AllAdFolders"
Write-Host "  bundled ads  $($adList.Count)"
Write-Host "  output       $publish"
Write-Host ""

Stop-BuildProcesses -Folder $publish

dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true `
    -p:Edition=$Edition `
    -p:FeatureAdsTab=$($AdsTab.ToString().ToLowerInvariant()) `
    -p:FeatureAdsSettings=$($AdsSettings.ToString().ToLowerInvariant()) `
    -p:FeatureAdvanced=$($Advanced.ToString().ToLowerInvariant()) `
    -p:FeatureAllAdFolders=$($AllAdFolders.ToString().ToLowerInvariant()) `
    -p:Version=$Version `
    -o $publish

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "==> Trimming runtimes we do not ship"
$trim = @(
    (Join-Path $publish "libvlc\win-arm64"),
    (Join-Path $publish "libvlc\win-x86"),
    (Join-Path $publish "runtimes\win-arm64"),
    (Join-Path $publish "runtimes\win-x86"),
    (Join-Path $publish "runtimes\vulkan\linux-x64")
)
foreach ($path in $trim) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

Write-Host "==> Bundling selected ads"
if (Test-Path -LiteralPath $adsTarget) {
    Remove-Item -LiteralPath $adsTarget -Recurse -Force
}

if ($adList.Count -gt 0) {
    New-Item -ItemType Directory -Force -Path $adsTarget | Out-Null
    $index = 0
    foreach ($file in $adList) {
        $index++
        $name = [System.IO.Path]::GetFileName($file)
        $destination = Join-Path $adsTarget $name
        if (Test-Path -LiteralPath $destination) {
            $destination = Join-Path $adsTarget ("{0:00}-{1}" -f $index, $name)
        }

        Copy-Item -LiteralPath $file -Destination $destination -Force
    }
}

$buildInfo = [ordered]@{
    edition = $Edition
    version = $Version
    features = [ordered]@{
        adsTab = $AdsTab
        adsSettings = $AdsSettings
        advanced = $Advanced
        allAdFolders = $AllAdFolders
    }
    ads = @($adList | ForEach-Object { [System.IO.Path]::GetFileName($_) })
    builtAt = (Get-Date).ToString("o")
}
$buildInfo | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $publish "build-info.json") -Encoding UTF8

if ($NoZip) {
    Write-Host ""
    Write-Host "Build ready: $publish"
    return
}

New-Item -ItemType Directory -Force -Path $release | Out-Null
$zip = Join-Path $release "TomatoFlow-$Version-$Edition-win-x64.zip"
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}

Write-Host "==> Zipping"
Stop-BuildProcesses -Folder $publish
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip -CompressionLevel Optimal

$size = [math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1)
Write-Host ""
Write-Host "Release ready: $zip ($size MB)"
