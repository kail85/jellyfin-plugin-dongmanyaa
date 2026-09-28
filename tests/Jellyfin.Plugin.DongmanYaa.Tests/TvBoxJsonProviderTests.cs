using System.Net;
using Jellyfin.Plugin.DongmanYaa.Models;
using Jellyfin.Plugin.DongmanYaa.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.DongmanYaa.Tests;

public sealed class TvBoxJsonProviderTests
{
    private const string SourceJson = """
        {"sources":[{"id":"free-one","name":"Free source","api":"https://catalog.example/api.php/provide/vod/","headers":{"User-Agent":"test-agent"}}]}
        """;

    [Fact]
    public void SourceParsingSkipsMalformedAndUnsupportedUrls()
    {
        Assert.Empty(ProviderSource.Parse("[]"));
        var sources = ProviderSource.Parse("""
            {"sources":[
              null,
              {"id":"ok_1","name":"Valid","api":"https://example.test/api"},
              {"id":7,"name":"Non-string identifier","api":"https://example.test/api"},
              {"id":"bad id","name":"Bad identifier","api":"https://example.test/api"},
              {"id":"local","name":"Not a URL","api":"file:///tmp/source"},
              {"id":"missing","api":"https://example.test/api"}]}
            """);

        Assert.Single(sources);
        Assert.Equal("ok_1", sources[0].Id);
    }

    [Fact]
    public async Task HomeAndCategoryParseCategoriesAndMetadata()
    {
        var handler = new StubHandler(_ => Json("""
            {"class":[{"type_id":"42","type_name":"Animation"}],"list":[{"vod_id":"v1","vod_name":"Sample show","vod_pic":"/poster.jpg","vod_year":"2024","vod_content":"Overview","type_name":"Series"}]}
            """));
        var provider = Create(handler);
        var source = Assert.Single(ProviderSource.Parse(SourceJson));

        var home = await provider.GetHomeAsync(source, CancellationToken.None);
        var category = await provider.GetCategoryItemsAsync(source, "42", 1, CancellationToken.None);

        Assert.Equal("Animation", Assert.Single(home.Categories).Name);
        var title = Assert.Single(home.Titles);
        Assert.Equal("Sample show", title.Name);
        Assert.Equal(2024, title.Year);
        Assert.Equal("Overview", title.Overview);
        Assert.Equal("https://catalog.example/poster.jpg", title.Poster?.AbsoluteUri);
        Assert.Single(category);
        Assert.Contains("t=42", handler.Requests[1]);
    }

    [Fact]
    public async Task SearchAndDetailsKeepEpisodeOrderAndAlternateDirectLines()
    {
        var handler = new StubHandler(uri => uri.Query.Contains("wd=", StringComparison.Ordinal)
            ? Json("""{"list":[{"vod_id":"v2","vod_name":"Search result"}]}""")
            : Json("""
                {"list":[{"vod_id":"v2","vod_name":"Series","type_name":"Series","vod_play_from":"main$$$backup","vod_play_url":"01$https://media.example/ep1.m3u8#02$https://media.example/ep2.m3u8$$$mirror$https://mirror.example/ep1.mp4#02$https://mirror.example/ep2.mp4"}]}
                """));
        var provider = Create(handler);
        var source = Assert.Single(ProviderSource.Parse(SourceJson));

        var found = await provider.SearchAsync(source, "show title", CancellationToken.None);
        var detail = await provider.GetDetailsAsync(source, "v2", CancellationToken.None);

        Assert.Equal("Search result", Assert.Single(found).Name);
        Assert.NotNull(detail);
        Assert.Collection(detail!.Episodes,
            first => { Assert.Equal("01", first.Name); Assert.Equal(2, first.Streams.Count); },
            second => { Assert.Equal("02", second.Name); Assert.Equal(2, second.Streams.Count); });
        Assert.Equal("main", detail.Episodes[0].Streams[0].Name);
        Assert.Equal("backup", detail.Episodes[0].Streams[1].Name);
        Assert.Contains("wd=show%20title", handler.Requests[0]);
        var playback = await provider.GetPlaybackSourcesAsync(source, "v2", 1, CancellationToken.None);
        Assert.Equal(2, playback.Count);
        var probe = new TvBoxJsonProvider(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("", System.Text.Encoding.UTF8, "application/vnd.apple.mpegurl"),
            })),
            NullLogger<TvBoxJsonProvider>.Instance);
        Assert.NotNull(await probe.ResolvePlaybackAsync(source, playback[0], CancellationToken.None));
        var loopback = playback[0] with { Url = new Uri("http://127.0.0.1/blocked.m3u8") };
        Assert.Null(await probe.ResolvePlaybackAsync(source, loopback, CancellationToken.None));
        var htmlProbe = new TvBoxJsonProvider(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("player page", System.Text.Encoding.UTF8, "text/html"),
            })),
            NullLogger<TvBoxJsonProvider>.Instance);
        Assert.Null(await htmlProbe.ResolvePlaybackAsync(source, playback[0], CancellationToken.None));
    }

    [Fact]
    public async Task DetailSkipsParserAndMalformedMediaAddresses()
    {
        var handler = new StubHandler(_ => Json("""
            {"list":[{"vod_id":"v3","vod_name":"Movie","type_name":"Movie","vod_play_from":"valid$$$protected$$$invalid","vod_play_url":"movie$https://media.example/movie.mp4$$$parse$parse:https://parser.example/?url=x$$$broken$javascript:alert(1)"}]}
            """));
        var detail = await Create(handler).GetDetailsAsync(Assert.Single(ProviderSource.Parse(SourceJson)), "v3", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.True(detail!.IsMovie);
        var stream = Assert.Single(Assert.Single(detail.Episodes).Streams);
        Assert.Equal("https://media.example/movie.mp4", stream.Url.AbsoluteUri);
    }

    [Fact]
    public async Task RetriesTransientFailureAndRejectsMalformedJson()
    {
        var attempt = 0;
        var retryHandler = new StubHandler(_ => ++attempt == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Json("""{"class":[],"list":[]}"""));
        var source = Assert.Single(ProviderSource.Parse(SourceJson));
        var home = await Create(retryHandler).GetHomeAsync(source, CancellationToken.None);
        Assert.Empty(home.Titles);
        Assert.Equal(2, attempt);

        var malformed = new StubHandler(_ => Json("not json"));
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => Create(malformed).GetHomeAsync(source, CancellationToken.None));
    }

    [Fact]
    public async Task RequestTimeoutPropagatesForPerSourceGracefulHandling()
    {
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json("{}");
        });
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) };
        var provider = new TvBoxJsonProvider(http, NullLogger<TvBoxJsonProvider>.Instance);
        var source = Assert.Single(ProviderSource.Parse(SourceJson));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetHomeAsync(source, CancellationToken.None));
    }

    [Fact]
    public async Task ForbiddenProviderIsCooledDownWithoutRepeatedRequests()
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });
        var provider = Create(handler);
        var source = Assert.Single(ProviderSource.Parse(SourceJson));

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetHomeAsync(source, CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetHomeAsync(source, CancellationToken.None));
        Assert.Equal(1, calls);
    }

    private static TvBoxJsonProvider Create(StubHandler handler) => new(new HttpClient(handler), NullLogger<TvBoxJsonProvider>.Instance);

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, System.Text.Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<Uri, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<Uri, CancellationToken, Task<HttpResponseMessage>> _asyncResponder = (uri, _) => Task.FromResult(responder(uri));
        private readonly List<string> _requests = [];

        public StubHandler(Func<Uri, CancellationToken, Task<HttpResponseMessage>> asyncResponder)
            : this(_ => throw new NotSupportedException()) => _asyncResponder = asyncResponder;

        public IReadOnlyList<string> Requests => _requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            _requests.Add(uri.AbsoluteUri);
            return await _asyncResponder(uri, cancellationToken);
        }
    }
}
