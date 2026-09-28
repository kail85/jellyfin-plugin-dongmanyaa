# Jellyfin 动漫鸭 integration research

This repository records analysis of the supplied 动漫鸭 10.1.0 APK and the compatibility assessment for a Jellyfin integration. It does **not** contain a functioning plugin: the APK does not bundle catalogue providers, and its source/config bootstrap is served by a signed app API. No source configuration was fetched or replayed.

Jellyfin 10.11.0 is installed on the NAS. A native channel plugin can expose a TV friendly folder hierarchy, but its channel interface does not provide a channel-local search field. A supported integration would need an independently and legitimately accessible provider configuration before implementation and live verification.

See [reverse-engineering notes](reverse-engineering/architecture.md) for the observed execution flow, [environment findings](docs/environment.md) for the NAS/Jellyfin state, and [source support matrix](docs/sources.md) for what can and cannot be used safely.

## Local APK

The original APK is retained at `reverse-engineering/dongmanyaa_10.1.0.apk` on the analysis NAS and excluded from Git. SHA256: `ccd266958681553217795d7f8633911c6f5b960a8f7f9041b2725422560e7c62`.

## Status

No Jellyfin files or services were modified. No plugin was installed. The existing Jellyfin instance still answers `/System/Info/Public`; the separate unauthenticated `/health` request timed out once during inspection and must be rechecked before any deployment. Docker socket access and root/service-management privileges are unavailable to the current account.
