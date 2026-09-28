using System.Text;
using Jellyfin.Plugin.DongmanYaa.Models;
using Jellyfin.Plugin.DongmanYaa.Providers;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DongmanYaa;

/// <summary>Exposes configured, direct TVBox JSON providers through ordinary Jellyfin channel navigation.</summary>
public sealed class DongmanYaaChannel(ITvBoxProvider provider, ILogger<DongmanYaaChannel> logger) : IChannel, IRequiresMediaInfoCallback
{
    public string Name => "动漫鸭";

    public string Description => "Browse configured public TVBox JSON catalogue sources.";

    public string DataVersion => "1";

    public string HomePageUrl => "https://github.com/kail85/jellyfin-plugin-dongmanyaa";

    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public bool IsEnabledFor(string userId) => true;

    public InternalChannelFeatures GetChannelFeatures() => new()
    {
        MediaTypes = [ChannelMediaType.Video],
        ContentTypes = [ChannelMediaContentType.Movie, ChannelMediaContentType.Episode],
        MaxPageSize = 100,
        SupportsContentDownloading = false,
    };

    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var sources = GetConfiguredSources();
        var folder = query.FolderId ?? string.Empty;
        if (folder.Length == 0)
        {
            return Result(sources.Select(ToSourceFolder));
        }

        try
        {
            if (TryReadSourceFolder(folder, out var sourceId))
            {
                var source = FindSource(sources, sourceId);
                var page = await provider.GetHomeAsync(source, cancellationToken).ConfigureAwait(false);
                var items = page.Categories.Select(category => ToCategoryFolder(source, category))
                    .Concat(page.Titles.Select(title => ToTitleFolder(source, title)));
                return Result(items);
            }

            if (TryReadCategoryFolder(folder, out sourceId, out var categoryId))
            {
                var source = FindSource(sources, sourceId);
                var titles = await provider.GetCategoryItemsAsync(source, categoryId, 1, cancellationToken).ConfigureAwait(false);
                return Result(titles.Select(title => ToTitleFolder(source, title)));
            }

            if (TryReadTitleFolder(folder, out sourceId, out var titleId))
            {
                var source = FindSource(sources, sourceId);
                var details = await provider.GetDetailsAsync(source, titleId, cancellationToken).ConfigureAwait(false);
                if (details is null)
                {
                    return Result([]);
                }

                if (details.IsMovie)
                {
                    return Result([ToMovie(source, details)]);
                }

                return Result(details.Episodes.Select(episode => ToEpisode(source, details.Title, episode)));
            }

            return Result([]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Catalogue request failed for folder {FolderId}", folder);
            return Result([]);
        }
    }

    public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
    {
        if (!TryReadMediaId(id, out var sourceId, out var titleId, out var episodeIndex))
        {
            return [];
        }

        try
        {
            var source = FindSource(GetConfiguredSources(), sourceId);
            var streams = await provider.GetPlaybackSourcesAsync(source, titleId, episodeIndex, cancellationToken).ConfigureAwait(false);
            var resolved = new List<MediaSourceInfo>();
            foreach (var stream in streams)
            {
                var playable = await provider.ResolvePlaybackAsync(source, stream, cancellationToken).ConfigureAwait(false);
                if (playable is not null)
                {
                    resolved.Add(ToMediaSource(playable, source.Api));
                }
            }

            return resolved;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Playback source resolution failed for channel item {ItemId}", id);
            return [];
        }
    }

    public Task<DynamicImageResponse?> GetChannelImage(ImageType type, CancellationToken cancellationToken) => Task.FromResult<DynamicImageResponse?>(null);

    public IEnumerable<ImageType> GetSupportedChannelImages() => [];

    private static ChannelItemInfo ToSourceFolder(ProviderSource source) => new()
    {
        Id = SourceFolderId(source.Id),
        Name = source.Name,
        Overview = "Browse the provider's categories and featured catalogue.",
        HomePageUrl = source.Api.AbsoluteUri,
        Type = ChannelItemType.Folder,
        FolderType = ChannelFolderType.Container,
        MediaType = ChannelMediaType.Video,
        ContentType = ChannelMediaContentType.Episode,
    };

    private static ChannelItemInfo ToCategoryFolder(ProviderSource source, ProviderCategory category) => new()
    {
        Id = CategoryFolderId(source.Id, category.Id),
        Name = category.Name,
        HomePageUrl = source.Api.AbsoluteUri,
        Type = ChannelItemType.Folder,
        FolderType = ChannelFolderType.Container,
        MediaType = ChannelMediaType.Video,
        ContentType = ChannelMediaContentType.Episode,
    };

    private static ChannelItemInfo ToTitleFolder(ProviderSource source, ProviderTitle title) => new()
    {
        Id = TitleFolderId(source.Id, title.Id),
        Name = title.Name,
        Overview = title.Overview,
        ImageUrl = title.Poster?.AbsoluteUri,
        HomePageUrl = source.Api.AbsoluteUri,
        ProductionYear = title.Year,
        Type = ChannelItemType.Folder,
        FolderType = ChannelFolderType.Container,
        MediaType = ChannelMediaType.Video,
        ContentType = ChannelMediaContentType.Episode,
    };

    private static ChannelItemInfo ToMovie(ProviderSource source, ProviderDetails details)
    {
        var first = details.Episodes.FirstOrDefault();
        return new ChannelItemInfo
        {
            Id = MediaItemId(source.Id, details.Title.Id, first?.Index ?? 1),
            Name = details.Title.Name,
            Overview = details.Title.Overview,
            ImageUrl = details.Title.Poster?.AbsoluteUri,
            ProductionYear = details.Title.Year,
            HomePageUrl = source.Api.AbsoluteUri,
            Type = ChannelItemType.Media,
            MediaType = ChannelMediaType.Video,
            ContentType = ChannelMediaContentType.Movie,
        };
    }

    private static ChannelItemInfo ToEpisode(ProviderSource source, ProviderTitle title, ProviderEpisode episode) => new()
    {
        Id = MediaItemId(source.Id, title.Id, episode.Index),
        Name = episode.Name,
        SeriesName = title.Name,
        IndexNumber = episode.Index,
        ParentIndexNumber = 1,
        Overview = title.Overview,
        ImageUrl = title.Poster?.AbsoluteUri,
        ProductionYear = title.Year,
        HomePageUrl = source.Api.AbsoluteUri,
        Type = ChannelItemType.Media,
        MediaType = ChannelMediaType.Video,
        ContentType = ChannelMediaContentType.Episode,
    };

    private static MediaSourceInfo ToMediaSource(ProviderStream stream, Uri referer)
    {
        var headers = new Dictionary<string, string>(stream.Headers, StringComparer.OrdinalIgnoreCase);
        headers.TryAdd("Referer", referer.AbsoluteUri);
        return new MediaSourceInfo
        {
            Id = stream.Id,
            Name = stream.Name,
            Path = stream.Url.AbsoluteUri,
            Protocol = MediaProtocol.Http,
            Container = Path.GetExtension(stream.Url.AbsolutePath).TrimStart('.'),
            IsRemote = true,
            SupportsDirectPlay = true,
            SupportsDirectStream = true,
            SupportsTranscoding = true,
            RequiredHttpHeaders = headers,
        };
    }

    private static ChannelItemResult Result(IEnumerable<ChannelItemInfo> items)
    {
        var all = items.ToArray();
        return new ChannelItemResult { Items = all, TotalRecordCount = all.Length };
    }

    private IReadOnlyList<ProviderSource> GetConfiguredSources()
    {
        try
        {
            return ProviderSource.Parse(Plugin.Instance?.Configuration.SourcesJson ?? "{}");
        }
        catch (System.Text.Json.JsonException exception)
        {
            logger.LogError(exception, "Provider configuration JSON is malformed");
            return [];
        }
    }

    private static ProviderSource FindSource(IReadOnlyList<ProviderSource> sources, string id) =>
        sources.FirstOrDefault(source => string.Equals(source.Id, id, StringComparison.Ordinal))
        ?? throw new InvalidOperationException("The configured provider no longer exists.");

    private static string SourceFolderId(string sourceId) => "src:" + Encode(sourceId);

    private static string CategoryFolderId(string sourceId, string categoryId) => "cat:" + Encode(sourceId) + ":" + Encode(categoryId);

    private static string TitleFolderId(string sourceId, string titleId) => "title:" + Encode(sourceId) + ":" + Encode(titleId);

    private static string MediaItemId(string sourceId, string titleId, int episodeIndex) =>
        $"media:{Encode(sourceId)}:{Encode(titleId)}:{episodeIndex}";

    private static bool TryReadSourceFolder(string value, out string sourceId) => TryReadOnePart(value, "src:", out sourceId);

    private static bool TryReadCategoryFolder(string value, out string sourceId, out string categoryId)
    {
        sourceId = categoryId = string.Empty;
        var parts = value.Split(':');
        return parts.Length == 3 && parts[0] == "cat" && TryDecode(parts[1], out sourceId) && TryDecode(parts[2], out categoryId);
    }

    private static bool TryReadTitleFolder(string value, out string sourceId, out string titleId)
    {
        sourceId = titleId = string.Empty;
        var parts = value.Split(':');
        return parts.Length == 3 && parts[0] == "title" && TryDecode(parts[1], out sourceId) && TryDecode(parts[2], out titleId);
    }

    private static bool TryReadMediaId(string value, out string sourceId, out string titleId, out int episodeIndex)
    {
        sourceId = titleId = string.Empty;
        episodeIndex = 0;
        var parts = value.Split(':');
        return parts.Length == 4 && parts[0] == "media" && TryDecode(parts[1], out sourceId)
            && TryDecode(parts[2], out titleId) && int.TryParse(parts[3], out episodeIndex) && episodeIndex > 0;
    }

    private static bool TryReadOnePart(string value, string prefix, out string part)
    {
        part = string.Empty;
        return value.StartsWith(prefix, StringComparison.Ordinal) && TryDecode(value[prefix.Length..], out part);
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecode(string value, out string decoded)
    {
        decoded = string.Empty;
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - (base64.Length % 4)) % 4);
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
