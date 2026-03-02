#!/usr/bin/env pwsh
# build.ps1 — Nexus System Monitor cross-platform build script
# Works on Windows, macOS, and Linux (PowerShell Core / pwsh)
#
# Usage:
#   ./build.ps1                          # build (default)
#   ./build.ps1 -Target test             # build + run tests
#   ./build.ps1 -Target publish          # publish for current OS
#   ./build.ps1 -Target publish-all      # publish all 6 RIDs (cross-compile)
#   ./build.ps1 -Target package          # publish + create dist/ archives
#   ./build.ps1 -Target clean            # remove bin/, obj/, publish/, dist/
#   ./build.ps1 -Target publish -Version 0.2.0  # override version

param(
    [string]$Target  = "build",
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$SolutionDir = $PSScriptRoot
$SolutionFile = Join-Path $SolutionDir "NexusMonitor.sln"
$UIProject = Join-Path $SolutionDir "src/NexusMonitor.UI/NexusMonitor.UI.csproj"
$DistDir = Join-Path $SolutionDir "dist"

# Detect current OS for platform-specific defaults
$IsWin = $IsWindows -or ($PSVersionTable.PSVersion.Major -lt 6 -and [System.Environment]::OSVersion.Platform -eq 'Win32NT')
$IsMac = $IsMacOS
$IsLnx = $IsLinux

# Profiles to publish for each platform
$PlatformProfiles = @{
    win   = @("win-x64", "win-arm64")
    osx   = @("osx-x64", "osx-arm64")
    linux = @("linux-x64", "linux-arm64")
}

$AllProfiles = $PlatformProfiles.win + $PlatformProfiles.osx + $PlatformProfiles.linux

# Version override args for dotnet publish
function Get-VersionArgs {
    if ($Version -ne "") { return "-p:Version=$Version" }
    return ""
}

function Invoke-DotNet {
    param([string[]]$Args)
    & dotnet @Args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Args[0]) failed (exit code $LASTEXITCODE)"
    }
}

function Invoke-Publish {
    param([string]$Profile)
    Write-Host "  → Publishing $Profile ..." -ForegroundColor Cyan
    $args = @(
        "publish", $UIProject,
        "-p:PublishProfile=$Profile",
        "--nologo"
    )
    $v = Get-VersionArgs
    if ($v -ne "") { $args += $v }
    Invoke-DotNet $args
}

function New-Archive {
    param([string]$Profile)
    $publishDir = Join-Path $SolutionDir "src/NexusMonitor.UI/publish/$Profile"
    if (!(Test-Path $publishDir)) {
        Write-Warning "  Skipping archive for $Profile — publish output not found"
        return
    }

    $ver = if ($Version -ne "") { $Version } else { "0.1.0" }
    $isWinRid = $Profile.StartsWith("win")

    if ($isWinRid) {
        $archiveName = "NexusMonitor-$ver-$Profile.zip"
        $archivePath = Join-Path $DistDir $archiveName
        Write-Host "  → Zipping $Profile → $archiveName" -ForegroundColor Cyan
        if (Test-Path $archivePath) { Remove-Item $archivePath -Force }
        Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $archivePath
    } else {
        $archiveName = "NexusMonitor-$ver-$Profile.tar.gz"
        $archivePath = Join-Path $DistDir $archiveName
        Write-Host "  → Tarring $Profile → $archiveName" -ForegroundColor Cyan
        if (Test-Path $archivePath) { Remove-Item $archivePath -Force }
        tar -czf $archivePath -C $publishDir .
    }
}

switch ($Target.ToLower()) {

    "build" {
        Write-Host "Building solution ..." -ForegroundColor Yellow
        Invoke-DotNet @("build", $SolutionFile, "--configuration", "Release", "--nologo")
        Write-Host "Build succeeded." -ForegroundColor Green
    }

    "test" {
        Write-Host "Building + testing solution ..." -ForegroundColor Yellow
        Invoke-DotNet @("build", $SolutionFile, "--configuration", "Release", "--nologo")
        Invoke-DotNet @(
            "test", $SolutionFile,
            "--no-build", "--configuration", "Release",
            "--logger", "console;verbosity=normal",
            "--nologo"
        )
        Write-Host "All tests passed." -ForegroundColor Green
    }

    "publish" {
        Write-Host "Publishing for current platform ..." -ForegroundColor Yellow
        $profiles = if ($IsWin)  { $PlatformProfiles.win   }
                    elseif ($IsMac)  { $PlatformProfiles.osx   }
                    else             { $PlatformProfiles.linux }
        foreach ($p in $profiles) { Invoke-Publish $p }
        Write-Host "Publish complete." -ForegroundColor Green
    }

    "publish-all" {
        Write-Host "Publishing all 6 platform RIDs (cross-compile) ..." -ForegroundColor Yellow
        foreach ($p in $AllProfiles) { Invoke-Publish $p }
        Write-Host "All publishes complete." -ForegroundColor Green
    }

    "package" {
        Write-Host "Publishing + packaging for current platform ..." -ForegroundColor Yellow
        New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

        $profiles = if ($IsWin)  { $PlatformProfiles.win   }
                    elseif ($IsMac)  { $PlatformProfiles.osx   }
                    else             { $PlatformProfiles.linux }

        foreach ($p in $profiles) {
            Invoke-Publish $p
            New-Archive $p
        }
        Write-Host "Packages written to: $DistDir" -ForegroundColor Green
    }

    "package-all" {
        Write-Host "Publishing + packaging all 6 platform RIDs ..." -ForegroundColor Yellow
        New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
        foreach ($p in $AllProfiles) {
            Invoke-Publish $p
            New-Archive $p
        }
        Write-Host "All packages written to: $DistDir" -ForegroundColor Green
    }

    "clean" {
        Write-Host "Cleaning build outputs ..." -ForegroundColor Yellow
        $toRemove = @(
            (Join-Path $SolutionDir "dist"),
            (Get-ChildItem $SolutionDir -Recurse -Filter "bin"  -Directory),
            (Get-ChildItem $SolutionDir -Recurse -Filter "obj"  -Directory),
            (Get-ChildItem $SolutionDir -Recurse -Filter "publish" -Directory)
        ) | Where-Object { $_ -ne $null }

        foreach ($item in $toRemove) {
            if (Test-Path $item) {
                Write-Host "  Removing $item" -ForegroundColor DarkGray
                Remove-Item -Recurse -Force $item
            }
        }
        Write-Host "Clean complete." -ForegroundColor Green
    }

    default {
        Write-Error "Unknown target: '$Target'. Valid targets: build, test, publish, publish-all, package, package-all, clean"
        exit 1
    }
}
