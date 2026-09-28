using Jellyfin.Plugin.DongmanYaa.Models;

namespace Jellyfin.Plugin.DongmanYaa.Providers;

public interface ITvBoxProvider
{
    Task<ProviderPage> GetHomeAsync(ProviderSource source, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderTitle>> GetCategoryItemsAsync(ProviderSource source, string categoryId, int page, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderTitle>> SearchAsync(ProviderSource source, string term, CancellationToken cancellationToken);

    Task<ProviderDetails?> GetDetailsAsync(ProviderSource source, string titleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderStream>> GetPlaybackSourcesAsync(ProviderSource source, string titleId, int episodeIndex, CancellationToken cancellationToken);

    Task<ProviderStream?> ResolvePlaybackAsync(ProviderSource source, ProviderStream stream, CancellationToken cancellationToken);
}
