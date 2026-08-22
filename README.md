# WoW ClassCodex Addon Downloader

This repository provides a downloader C# program for the ClassCodex Addon for World of Warcraft, based on gable44 Python version (https://github.com/gable44/WoWClassCodexDownloader)

# ClassCodex Downloader

A simple downloader program for the [ClassCodex](https://addons.wago.io/addons/classcodex) Addon for World of Warcraft. (https://www.icy-veins.com/download)

The program downloads the latest production build of ClassCodex, verifies the downloaded files using SHA-256 checksums, and installs or updates the addon in your AddOns folder.

## Features

* Downloads the latest ClassCodex production build
* Verifies the manifest using SHA-256
* Verifies every downloaded file using SHA-256
* Skips files that are already up to date
* Prevents unsafe manifest paths
* Uses temporary files during downloads to avoid incomplete files
* Automatically discovers the default WoW AddOns path on Windows using the registry or Battle.net configuration

## Requirements

* .NET 10.0 SDK
* World of Warcraft game client

The program HMI uses Avalonia Framework that is resolved via NuGet directly. The reason is to be executable under Linux.

## Configuration

You can configure `AddonsPath` in `appsettings.json`.

By default, on Windows, the app tries to discover the WoW AddOns path automatically (registry first, then Battle.net configuration files). If discovery is successful, that discovered path is used as the default.

`appsettings.json` still has priority when you intentionally set a custom path. If the saved value is still the legacy default (`C:\Program Files (x86)\World of Warcraft\_retail_\Interface\AddOns`), the app treats it as non-custom and uses the discovered path when available.

In the HMI version, you can set the path in the window and it is saved for future runs.
In the CLI app, set `AddonsPath` in `appsettings.json` when you want to force a custom location.

## Usage
Run the HMI program. The AddOns path is auto-detected when possible, and any path you set in the UI is remembered for the next execution.
Or run the CLI program in a terminal:

`ClassCodexDownloaderCli` (no arguments).

The program will:
1. Download the current ClassCodex channel configuration.
2. Download and verify the manifest.
3. Verify that the manifest belongs to ClassCodex Retail.
4. Compare local files with the expected SHA-256 hashes.
5. Download missing or outdated files.
6. Verify every downloaded file.
7. Report the installed build and download results.

## Security
The downloader performs integrity checks before installing files.
The channel configuration provides the expected SHA-256 hash of the manifest. The manifest then provides the expected SHA-256 hash and file size for each addon file.
Downloaded files are verified before they are considered successfully installed.
The program also rejects manifest paths containing unsafe path traversal components.

## Disclaimer
This is an independent downloader program for the ClassCodex Addon.
World of Warcraft and related trademarks are property of their respective owners. This project is not affiliated with or endorsed by Blizzard Entertainment unless explicitly stated otherwise.
The ClassCodex addon, its data, and its distribution infrastructure may be subject to their own licenses and terms. This repository's license applies only to the code contained in this repository.