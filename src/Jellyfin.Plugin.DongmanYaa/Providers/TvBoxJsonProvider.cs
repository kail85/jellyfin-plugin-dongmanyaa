using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.DongmanYaa.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DongmanYaa.Providers;

/// <summary>Reads the conventional TVBox type=1 JSON API. It never loads scripts, spiders or parser URLs.</summary>
public sealed class TvBoxJsonProvider(HttpClient httpClient, ILogger<TvBoxJsonProvider> logger) : ITvBoxProvider
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expires, string Body)> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sourceLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cooldowns = new(StringComparer.Ordinal);

    public async Task<ProviderPage> GetHomeAsync(ProviderSource source, CancellationToken cancellationToken)
    {
        using var document = await RequestJsonAsync(source, "ac=list&pg=1", cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var categories = ReadArray(root, "class")
            .Select(item => new ProviderCategory(ReadString(item, "type_id"), ReadString(item, "type_name")))
            .Where(item => item.Id.Length > 0 && item.Name.Length > 0)
            .ToArray();
        return new ProviderPage(categories, ParseTitles(ReadArray(root, "list"), source));
    }

    public async Task<IReadOnlyList<ProviderTitle>> GetCategoryItemsAsync(ProviderSource source, string categoryId, int page, CancellationToken cancellationToken)
    {
        var query = $"ac=list&pg={Math.Clamp(page, 1, 1000).ToString(CultureInfo.InvariantCulture)}&t={Uri.EscapeDataString(categoryId)}";
        using var document = await RequestJsonAsync(source, query, cancellationToken).ConfigureAwait(false);
        return ParseTitles(ReadArray(document.RootElement, "list"), source);
    }

    public async Task<IReadOnlyList<ProviderTitle>> SearchAsync(ProviderSource source, string term, CancellationToken cancellationToken)
    {
        var query = "ac=videolist&wd=" + Uri.EscapeDataString(term.Trim());
        using var document = await RequestJsonAsync(source, query, cancellationToken).ConfigureAwait(false);
        return ParseTitles(ReadArray(document.RootElement, "list"), source);
    }

    public async Task<ProviderDetails?> GetDetailsAsync(ProviderSource source, string titleId, CancellationToken cancellationToken)
    {
        var query = "ac=detail&ids=" + Uri.EscapeDataString(titleId);
        using var document = await RequestJsonAsync(source, query, cancellationToken).ConfigureAwait(false);
        var item = ReadArray(document.RootElement, "list").FirstOrDefault();
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = ParseTitle(item, source);
        var flags = ReadString(item, "vod_play_from").Split("$$$", StringSplitOptions.None);
        var playGroups = ReadString(item, "vod_play_url").Split("$$$", StringSplitOptions.None);
        var episodes = new SortedDictionary<int, (string Name, List<ProviderStream> Streams)>();
        for (var lineIndex = 0; lineIndex < Math.Min(flags.Length, playGroups.Length); lineIndex++)
        {
            var lineName = string.IsNullOrWhiteSpace(flags[lineIndex]) ? $"Source {lineIndex + 1}" : flags[lineIndex].Trim();
            var entries = playGroups[lineIndex].Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var episodeIndex = 0; episodeIndex < entries.Length; episodeIndex++)
            {
                var separator = entries[episodeIndex].IndexOf('$');
                var episodeName = separator < 0 ? $"Episode {episodeIndex + 1}" : entries[episodeIndex][..separator].Trim();
                var address = separator < 0 ? entries[episodeIndex].Trim() : entries[episodeIndex][(separator + 1)..].Trim();
                if (!TryGetDirectMediaUri(address, source.Api, out var mediaUri))
                {
                    logger.LogDebug("Skipping a non-direct or invalid stream from source {SourceId} for title {TitleId}", source.Id, title.Id);
                    continue;
                }

                if (!episodes.TryGetValue(episodeIndex + 1, out var episode))
                {
                    episode = (episodeName, []);
                    episodes.Add(episodeIndex + 1, episode);
                }

                episode.Streams.Add(new ProviderStream(
                    $"{source.Id}-{title.Id}-{episodeIndex + 1}-{lineIndex + 1}",
                    lineName,
                    mediaUri,
                    source.Headers));
                episodes[episodeIndex + 1] = episode;
            }
        }

        var parsedEpisodes = episodes.Select(pair => new ProviderEpisode(
            string.IsNullOrWhiteSpace(pair.Value.Name) ? $"Episode {pair.Key}" : pair.Value.Name,
            pair.Key,
            pair.Value.Streams)).ToArray();
        var typeName = title.TypeName ?? string.Empty;
        var isMovie = parsedEpisodes.Length == 1
            && (typeName.Contains("电影", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("movie", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("剧场版", StringComparison.OrdinalIgnoreCase));
        return new ProviderDetails(title, isMovie, parsedEpisodes);
    }

    public async Task<IReadOnlyList<ProviderStream>> GetPlaybackSourcesAsync(ProviderSource source, string titleId, int episodeIndex, CancellationToken cancellationToken)
    {
        var details = await GetDetailsAsync(source, titleId, cancellationToken).ConfigureAwait(false);
        return details?.Episodes.FirstOrDefault(episode => episode.Index == episodeIndex)?.Streams ?? [];
    }

    public Task<ProviderStream?> ResolvePlaybackAsync(ProviderSource source, ProviderStream stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var valid = (stream.Url.Scheme == Uri.UriSchemeHttp || stream.Url.Scheme == Uri.UriSchemeHttps)
            && !stream.Url.IsLoopback;
        return ResolvePlaybackCoreAsync(source, stream, valid, cancellationToken);
    }

    private async Task<ProviderStream?> ResolvePlaybackCoreAsync(ProviderSource source, ProviderStream stream, bool valid, CancellationToken cancellationToken)
    {
        if (!valid)
        {
            return null;
        }

        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, stream.Url);
            AddPlaybackHeaders(head, source, stream);
            using var response = await httpClient.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is System.Net.HttpStatusCode.MethodNotAllowed or System.Net.HttpStatusCode.NotImplemented)
            {
                using var get = new HttpRequestMessage(HttpMethod.Get, stream.Url);
                get.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                AddPlaybackHeaders(get, source, stream);
                using var rangeResponse = await httpClient.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                return IsPlayableResponse(rangeResponse, stream.Url) ? stream : null;
            }

            return IsPlayableResponse(response, stream.Url) ? stream : null;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogInformation(exception, "Direct media probe failed for source {SourceId}", source.Id);
            return null;
        }
    }

    private static void AddHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static void AddPlaybackHeaders(HttpRequestMessage request, ProviderSource source, ProviderStream stream)
    {
        AddHeaders(request, stream.Headers);
        if (!request.Headers.Contains("Referer"))
        {
            request.Headers.Referrer = source.Api;
        }
    }

    private static bool IsPlayableResponse(HttpResponseMessage response, Uri uri)
    {
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("dash+xml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        return extension.Equals(".m3u8", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mpd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".flv", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<JsonDocument> RequestJsonAsync(ProviderSource source, string query, CancellationToken cancellationToken)
    {
        var uri = BuildRequestUri(source.Api, query);
        var headerFingerprint = string.Join('&', source.Headers.OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .Select(header => header.Key + "=" + header.Value));
        var cacheKey = source.Id + "|" + uri.AbsoluteUri + "|" + headerFingerprint;
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
        {
            return JsonDocument.Parse(cached.Body);
        }

        if (_cooldowns.TryGetValue(source.Id, out var cooldownUntil) && cooldownUntil > DateTimeOffset.UtcNow)
        {
            throw new HttpRequestException($"Provider {source.Id} is cooling down after a recent failure.");
        }

        var sourceLock = _sourceLocks.GetOrAdd(source.Id, _ => new SemaphoreSlim(1, 1));
        await sourceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached) && cached.Expires > DateTimeOffset.UtcNow)
            {
                return JsonDocument.Parse(cached.Body);
            }

            if (_cooldowns.TryGetValue(source.Id, out cooldownUntil) && cooldownUntil > DateTimeOffset.UtcNow)
            {
                throw new HttpRequestException($"Provider {source.Id} is cooling down after a recent failure.");
            }

            try
            {
                for (var attempt = 0; ; attempt++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    AddHeaders(request, source.Headers);

                    try
                    {
                        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                        if (attempt == 0 && ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500))
                        {
                            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        response.EnsureSuccessStatusCode();
                        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        using var validation = JsonDocument.Parse(body);
                        if (_cache.Count > 128)
                        {
                            _cache.Clear();
                        }

                        _cache[cacheKey] = (DateTimeOffset.UtcNow + CacheLifetime, body);
                        _cooldowns.TryRemove(source.Id, out _);
                        return JsonDocument.Parse(body);
                    }
                    catch (HttpRequestException exception) when (attempt == 0 && IsTransient(exception))
                    {
                        await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is HttpRequestException or JsonException or TaskCanceledException)
            {
                _cooldowns[source.Id] = DateTimeOffset.UtcNow + FailureCooldown;
                logger.LogWarning(exception, "TVBox JSON request failed for source {SourceId} ({Path}); cooling it down briefly", source.Id, uri.AbsolutePath);
                throw;
            }
        }
        finally
        {
            sourceLock.Release();
        }
    }

    private static Uri BuildRequestUri(Uri api, string query)
    {
        var builder = new UriBuilder(api);
        var oldQuery = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrEmpty(oldQuery) ? query : oldQuery + "&" + query;
        return builder.Uri;
    }

    private static bool IsTransient(HttpRequestException exception) =>
        exception.StatusCode is null
        || exception.StatusCode == System.Net.HttpStatusCode.RequestTimeout
        || exception.StatusCode == System.Net.HttpStatusCode.TooManyRequests
        || (int)exception.StatusCode >= 500;

    private static JsonElement[] ReadArray(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : [];

    private static string ReadString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            _ => string.Empty,
        };
    }

    private static IReadOnlyList<ProviderTitle> ParseTitles(IEnumerable<JsonElement> elements, ProviderSource source) =>
        elements.Where(item => item.ValueKind == JsonValueKind.Object).Select(item => ParseTitle(item, source)).Where(title => title.Id.Length > 0 && title.Name.Length > 0).ToArray();

    private static ProviderTitle ParseTitle(JsonElement item, ProviderSource source)
    {
        var poster = ReadString(item, "vod_pic");
        Uri? posterUri = null;
        if (!string.IsNullOrWhiteSpace(poster) && Uri.TryCreate(source.Api, poster, out var parsedPoster)
            && (parsedPoster.Scheme == Uri.UriSchemeHttp || parsedPoster.Scheme == Uri.UriSchemeHttps))
        {
            posterUri = parsedPoster;
        }

        var yearText = ReadString(item, "vod_year");
        int? year = int.TryParse(yearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedYear) ? parsedYear : null;
        var overview = ReadString(item, "vod_content");
        return new ProviderTitle(
            ReadString(item, "vod_id"),
            ReadString(item, "vod_name"),
            string.IsNullOrWhiteSpace(overview) ? null : overview,
            posterUri,
            year,
            ReadString(item, "type_name"));
    }

    private static bool TryGetDirectMediaUri(string address, Uri baseUri, out Uri mediaUri)
    {
        mediaUri = baseUri;
        if (address.Contains("@@", StringComparison.Ordinal) || address.StartsWith("parse:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUri, address, out var candidate)
            || (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        mediaUri = candidate;
        return true;
    }
}
