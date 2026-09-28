# Validation status

Run `./scripts/test.sh` for the local unit suite and `./scripts/package.sh` to produce the Jellyfin plugin zip. Unit coverage currently verifies source config parsing, category/title metadata, search query parsing, ordered episodes, multiple source lines, direct media response validation, rejection of HTML/parser and loopback URLs, transient HTTP retry, malformed JSON, timeout propagation, and brief per-provider cooldown after failure.

The GitHub Actions workflow runs `dotnet test` and `dotnet build` for pull requests and pushes. The ignored APK is not needed by CI.

Live source tests are intentionally not run: the APK supplies no independent source endpoints, and obtaining its dynamic catalogue requires its signed bootstrap. No legitimate direct provider endpoint was available to test. Jellyfin install, channel browse/search, playback-info, playback, seek, watch-state, and resume tests are also not complete because no source is configured, the Jellyfin plugin directory is not writable by the current account, and the unauthenticated plugin API returns 401.
