using System.Text.Json;

namespace Jellyfin.Plugin.DongmanYaa.Models;

public sealed record ProviderSource(string Id, string Name, Uri Api, IReadOnlyDictionary<string, string> Headers)
{
    public static IReadOnlyList<ProviderSource> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (!document.RootElement.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<ProviderSource>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in sources.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!TryReadString(element, "id", out var id)
                || !TryReadString(element, "name", out var name)
                || !TryReadString(element, "api", out var apiValue))
            {
                continue;
            }

            id = id.Trim();
            name = name.Trim();
            apiValue = apiValue.Trim();
            if (string.IsNullOrWhiteSpace(id) || !IsSafeId(id) || string.IsNullOrWhiteSpace(name)
                || !Uri.TryCreate(apiValue, UriKind.Absolute, out var api)
                || (api.Scheme != Uri.UriSchemeHttps && api.Scheme != Uri.UriSchemeHttp)
                || api.UserInfo.Length != 0
                || !seenIds.Add(id))
            {
                continue;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (element.TryGetProperty("headers", out var headerElement) && headerElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var header in headerElement.EnumerateObject())
                {
                    if (!string.IsNullOrWhiteSpace(header.Name) && header.Value.ValueKind == JsonValueKind.String)
                    {
                        headers[header.Name] = header.Value.GetString() ?? string.Empty;
                    }
                }
            }

            result.Add(new ProviderSource(id, name, api, headers));
        }

        return result;
    }

    private static bool TryReadString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(property, out var candidate) || candidate.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = candidate.GetString() ?? string.Empty;
        return true;
    }

    private static bool IsSafeId(string id) => id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public sealed record ProviderCategory(string Id, string Name);

public sealed record ProviderTitle(string Id, string Name, string? Overview, Uri? Poster, int? Year, string? TypeName);

public sealed record ProviderStream(string Id, string Name, Uri Url, IReadOnlyDictionary<string, string> Headers);

public sealed record ProviderEpisode(string Name, int Index, IReadOnlyList<ProviderStream> Streams);

public sealed record ProviderDetails(ProviderTitle Title, bool IsMovie, IReadOnlyList<ProviderEpisode> Episodes);

public sealed record ProviderPage(IReadOnlyList<ProviderCategory> Categories, IReadOnlyList<ProviderTitle> Titles);
