---
name: plexlyricsync-testing
description: Validate PlexLyricSync after code changes in the local Windows repo. Use when Codex needs to build the .NET solution, run unit and integration tests, package the WinUI/Windows App SDK app as MSIX, install or relaunch the packaged app, or explain what can and cannot be visually confirmed from Codex.
---

# PlexLyricSync Testing

## Overview

Use this workflow after making changes to PlexLyricSync. Prefer live commands over assumptions and report exact pass/fail output, skipped tests, warnings that matter, and whether the packaged app process is running.

## Baseline Checks

Run commands from the repo root

Confirm the SDK/runtime context when launch behavior is in question:

```powershell
dotnet --info
```

## Build And Test

Use the solution test command first:

```powershell
dotnet test PlexLyricSync.sln --configuration Debug --no-restore
```

Expected healthy result from the validated local workflow:

- `PlexLyricSync.Core` builds.
- `PlexLyricSync.Tests` runs with passing tests.
- `PlexLyricSync.IntegrationTests` builds, with passing tests.
- Exit code is `0`.

Mention non-blocking warnings only when useful. Known warnings include nullable annotations in files without nullable context, async test methods without `await`, and MSTest parallelization configuration.

## Direct App Launch

Do not rely on plain `dotnet run` as the primary app launch path. The project is a packaged Windows App SDK/MSIX app, so default launch can fail with:

- `Packaged .NET applications with an app host exe cannot be ProcessorArchitecture neutral`
- `Class not registered (0x80040154)` from Windows App Runtime deployment initialization

If trying direct launch for diagnosis, use the x64 platform on this machine:

```powershell
dotnet run --project PlexLyricSync\PlexLyricSync.csproj --configuration Debug --no-restore -p:Platform=x64
```

If the Windows App Runtime is missing, install/register the matching runtime for the package reference in `PlexLyricSync\PlexLyricSync.csproj`. For `Microsoft.WindowsAppSDK` `1.7.250606001`, use Windows App Runtime 1.7:

```powershell
winget install --id Microsoft.WindowsAppRuntime.1.7 -e
```

Or use Microsoft's Windows App SDK downloads page and run the 1.7 `WindowsAppRuntimeInstall.exe` as Administrator.

## Packaged Deploy And Launch

Prefer the repo script for real app launch testing:

```powershell
Tools\deploy-packaged.ps1
```

This script:

1. Publishes an x64 Debug MSIX with `dotnet publish`.
2. Finds the newest `.msix` or `.msixbundle` under the app package directory.
3. Removes any existing package whose name matches `*PlexLyricSync*`.
4. Installs the new package with `Add-AppxPackage`.
5. Launches the app through `explorer.exe shell:AppsFolder\<AppID>`.

Running this script modifies app package registration and launches a GUI app, so request elevated/out-of-sandbox permission when required by the execution environment.

Successful output should include:

- A generated `.msix` or `.msixbundle` path under `PlexLyricSync\AppPackages\`.
- Removal of any existing installed PlexLyricSync package.
- Installation of the new package with `Add-AppxPackage`.
- Launch through an AppID similar to `PlexLyricSync_<publisher-id>!App`.

Warnings about missing `win-AnyCPU.pubxml` and missing `mspdbcmf.exe` for symbol package generation did not block package deployment or launch.

## Runtime Confirmation

After deploying or launching, confirm the process is alive:

```powershell
Get-Process | Where-Object { $_.ProcessName -like '*PlexLyricSync*' -or $_.MainWindowTitle -like '*PlexLyricSync*' } | Select-Object ProcessName, Id, MainWindowTitle, Responding
```

A validated launch showed:

- `ProcessName`: `PlexLyricSync`
- `Responding`: `True`
- `MainWindowTitle`: empty

Do not claim to see native desktop UI contents unless a visual inspection tool is actually available. Codex can confirm the package installed, the app launched, and the process is responding; it cannot confirm on-screen UI content from process metadata alone.
