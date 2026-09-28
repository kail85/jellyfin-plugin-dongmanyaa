# APK reverse engineering notes

## Artifact and method

- Input: `动漫鸭_10.1.0.apk`, downloaded from the user supplied GitHub URL.
- SHA256: `ccd266958681553217795d7f8633911c6f5b960a8f7f9041b2725422560e7c62`.
- Size: 13,599,355 bytes (about 13 MiB).
- Manifest and resources decoded with JADX 1.5.6; both `classes.dex` and `classes2.dex` decompiled. APK contents and native library names inspected with `unzip` and `strings`.
- APK is an Android TV app (`LEANBACK_LAUNCHER`) named 动漫鸭, package namespace `com.github.tvbox.osc`, with app-specific code under `androidx.base`.

## Lineage and bundled material

This is derived from TVBox OSC and embeds the CatVod crawler runtime (`com.github.catvod.crawler.JarLoader`, `JsLoader`, `Spider`). It includes QuickJS (`libquickjs.so`) and JavaScript helper libraries (Cheerio, CryptoJS, Day.js, URI.js, Underscore). It also has an app-owned native player and SQLite/Room persistence for `vodRecord` and `vodCollect`.

The APK does not contain a default TVBox source JSON or provider catalogue. Bundled `assets/epg_data.json` is EPG data; `assets/ua.db` is a user-agent database. Source/site definitions and spider configuration are fetched remotely at runtime. The APK also has a `csp.jar` loading path and support for `.js` spiders. Those executable provider implementations are not ordinary static data.

## Observed startup and source flow

1. `StartActivity.onCreate` starts separate requests for `site`, `exten`, `ini` and `level`.
2. `androidx.base.iu.h(action)` builds `http://tvbox.k8aa.com/api.php?act=<action>&app=10000`.
3. The `site` request adds `t` (Unix seconds) and `sign`. `iu.i` derives the signature from the timestamp, app identifier, and app-key material embedded in the app. The signature is an access mechanism; it was inspected but not generated or replayed.
4. `ini` supplies initialization data. `HomeActivity` reads `InitBean.msg.appJsonb` and stores it as `json_url`.
5. The app retrieves and parses that JSON configuration. `androidx.base.ci.k` builds the source map, selects a home source and records global parse definitions, flags, live configuration, and spider path. Source config can be an HTTP URL or a `clan://` resource; the app has special handling for local and packaged files.
6. Site records (`SiteBean.MsgDTO`) carry `gname`, `gid`, `gtype`, `gapiname`, `extend`, `parse`, `searchable`, and `quicksearch`. They are mapped into source objects with API URL, type, extensions and search flags.
7. The TVBox source/spider contract supplies home, category, detail, search and player operations. The APK delegates spider operations to CatVod JAR or JS loaders when configured, while other providers expose API/HTML endpoints. Site-level parser rules and global parse definitions are represented separately.
8. Detail UI holds the selected site/source key, VOD id, play flag and episode index. A title can expose multiple source sites; a source can expose multiple play flags/lines and ordered episode URLs. The player can pass ordinary user-agent/accept headers, use configured parsing rules, and load direct HLS/MP4 URLs or an embedded web player. Resolver selection and provider results depend on remotely supplied config and executable spiders.
9. `vodRecord` and `vodCollect` persist playback-history/resume-related records and favourites locally in the app. These are not portable Jellyfin metadata.

The APK strings include `/catvod_csp`, `/.tvbox_folder`, `.tvbox_folder`, and `/tvbox_backup/`. These are supporting markers, not bundled provider catalogues.

## Jellyfin mapping considerations

A clean adapter could map title -> series/movie, seasons/episodes -> Jellyfin episode hierarchy and provider lines -> alternate media sources. Direct HTTP media with ordinary source headers can use native Jellyfin media-source callbacks. Jellyfin would own user history and resume state. Arbitrary JAR/JS spider execution would require a separate sandbox/runtime strategy and is not needed for a direct API provider.

Jellyfin 10.11.0's native `IChannel` contract is browse-oriented and has no channel-local text query field. Search in a stock Jellyfin client therefore cannot be assumed. The exact installed API contracts were previously inspected in the NAS's 10.11.0 assemblies and in the existing `jellyfin-ikan` worktree.

## Safety boundary and conclusion

No providers or playable media URLs are bundled in the APK. The source list and active provider configuration require the signed app bootstrap described above, and downloaded JAR/JS spiders may execute provider code. This analysis did not contact that API, derive/replay its signature, fetch its configuration, load spiders, or test a playback resolver. Consequently there is no provider for which catalogue/search/detail/playback access can be established as legitimate and unauthenticated from the supplied artifact alone. A Jellyfin plugin with no verified provider would be a nonfunctional shell, so none was installed.
