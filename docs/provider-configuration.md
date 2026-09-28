# Provider configuration

The plugin supports ordinary type=1 TVBox JSON APIs that return `class` and `list` arrays for `ac=list`, return `list` for `ac=videolist&wd=...`, and expose details with `ac=detail&ids=...`. Detail records must include `vod_id`, `vod_name`, `vod_play_from` and `vod_play_url`. Playback URLs must be direct HTTP(S) media URLs. Parsing services and indirect resolver URLs are skipped.

Enter JSON in the plugin configuration page:

```json
{
  "sources": [
    {
      "id": "authorized-catalogue",
      "name": "My authorized catalogue",
      "api": "https://catalogue.example/api.php/provide/vod/",
      "headers": {
        "User-Agent": "Jellyfin"
      }
    }
  ]
}
```

The `id` must contain only ASCII letters, digits, `_` and `-`. `api` must be an absolute HTTP(S) URL. `headers` are optional and are sent to that configured provider; treat the settings as sensitive if they contain credentials. The plugin does not persist cookies, sign app requests, evaluate scripts, run JARs, or resolve parser services.

Browse flow is provider → categories and featured titles → title → episode → play. Multiple direct lines for the same episode become alternate Jellyfin media sources, with provider line order preserved. Jellyfin owns playback history and resume state when a client reports progress; this has not yet been verified with a live provider/client. Movies are exposed as playable movie items; series episodes have stable order numbers and series metadata.

Provider `SearchAsync` parses the standard search endpoint, but Jellyfin 10.11.0 `IChannel` does not expose a query field to stock clients. A search box inside this channel is therefore unavailable without a client-specific UI.
