# Source support matrix

| Source category | Evidence in APK | Status | Reason |
|---|---|---|---|
| Bundled public catalogue/provider | None found | Unsupported | No source definitions or active provider URLs are bundled. |
| TVBox API / HTML providers | Dynamic `site` and JSON config model | Unsupported pending independent access | Runtime configuration comes from a signed app API; no source endpoint was independently authorized or verified. |
| CatVod JAR spiders | `JarLoader`, `csp.jar` path | Skipped | Executable code is dynamically downloaded. No provider jar was loaded or audited. |
| JavaScript spiders | `JsLoader`, QuickJS, bundled helper libraries | Skipped | Dynamic source scripts are not bundled and can execute provider logic. |
| `clan://` local sources | Explicit special handling in `ci` | Unsupported | Device-local configuration is unavailable on Jellyfin and was not present in the APK. |
| Global media parsers | Parser records in config model | Skipped | Definitions are remote and resolver behavior cannot be verified independently. |
| Direct HLS/MP4 URLs | Player supports ordinary URLs | Unsupported pending source | No legitimate direct media URL was present in the artifact. |

No provider has been marked supported because that requires live catalogue, metadata, episode and media-response evidence from the NAS without protected access.
