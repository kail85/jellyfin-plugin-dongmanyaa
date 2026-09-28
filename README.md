# Jellyfin 动漫鸭 integration research

This repository contains the APK analysis and a native Jellyfin channel plugin for explicitly configured TVBox-compatible JSON providers. The APK does **not** bundle its catalogue providers; its source/config bootstrap is served by a signed app API, which this plugin does not access. No source configuration was fetched or replayed. There are no default providers, so the channel stays empty until an independently accessible provider is configured.

Jellyfin 10.11.0 is installed on the NAS. A native channel plugin exposes provider/category/title/episode folders, direct alternate media sources, posters and metadata. Its `IChannel` interface does not provide a channel-local search field; provider search parsing is implemented, but stock clients cannot submit a search term to this channel. A provider source must be independently and legitimately accessible before live verification.

See [reverse-engineering notes](reverse-engineering/architecture.md) for the observed execution flow, [provider configuration](docs/provider-configuration.md), [environment findings](docs/environment.md), and [source support matrix](docs/sources.md).
Validation limitations are listed in [tests](docs/tests.md).

## Build and test

The project targets the installed Jellyfin 10.11.0 API and .NET 9.

```sh
./scripts/test.sh
./scripts/package.sh
```

## Configure

In Jellyfin Dashboard → Plugins → 动漫鸭 settings, add only providers you are authorized to use. See [provider configuration](docs/provider-configuration.md). The standard Jellyfin channel UI supports browsing; search input is unavailable in the native channel contract for this Jellyfin version.

## Deploy

`sudo ./scripts/deploy.sh` tests and packages the plugin, backs up the prior plugin folder, replaces only this plugin, restarts Jellyfin, checks health and load logs, and restores the backup if verification fails. The current NAS shell account lacks the required root privileges, so deployment has not been run.

## Local APK

The original APK is retained at `reverse-engineering/dongmanyaa_10.1.0.apk` on the analysis NAS and excluded from Git. SHA256: `ccd266958681553217795d7f8633911c6f5b960a8f7f9041b2725422560e7c62`.

## Status

The plugin builds and its provider parser tests run locally. No Jellyfin files or services were modified and the plugin is not installed. No live source is configured, so catalogue/playback and Jellyfin client behavior remain unverified. Docker socket access and root/service-management privileges are unavailable to the current account.
