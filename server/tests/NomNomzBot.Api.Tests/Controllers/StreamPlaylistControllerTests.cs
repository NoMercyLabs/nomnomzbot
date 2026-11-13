// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Net;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Infrastructure.Platform.Transport.LivePlaylist;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The live playlist proxy the dashboard's player reads a Twitch live stream through: the real controller over the
/// real proxy service, with only Twitch's HTTP side stubbed. The encoded addresses are the player's own form
/// (base64url of the address, no padding), computed once and written out, so a test never re-implements it.
/// </summary>
public sealed class StreamPlaylistControllerTests
{
    // https://usher.ttvnw.net/api/channel/hls/stoney_eagle.m3u8?allow_source=true&sig=ab%2Bc&token=%7B%7D
    private const string UsherEncoded =
        "aHR0cHM6Ly91c2hlci50dHZudy5uZXQvYXBpL2NoYW5uZWwvaGxzL3N0b25leV9lYWdsZS5tM3U4P2FsbG93X3NvdXJjZT10cnVlJnNpZz1hYiUyQmMmdG9rZW49JTdCJTdE";
    private const string UsherAddress =
        "https://usher.ttvnw.net/api/channel/hls/stoney_eagle.m3u8?allow_source=true&sig=ab%2Bc&token=%7B%7D";

    // https://video-weaver.ams03.hls.ttvnw.net/v1/playlist/Cq0FAbc.m3u8
    private const string VariantEncoded =
        "aHR0cHM6Ly92aWRlby13ZWF2ZXIuYW1zMDMuaGxzLnR0dm53Lm5ldC92MS9wbGF5bGlzdC9DcTBGQWJjLm0zdTg";

    // https://video-weaver.ams03.hls.ttvnw.net/v1/playlist/Audio1.m3u8
    private const string AudioEncoded =
        "aHR0cHM6Ly92aWRlby13ZWF2ZXIuYW1zMDMuaGxzLnR0dm53Lm5ldC92MS9wbGF5bGlzdC9BdWRpbzEubTN1OA";

    private const string MasterPlaylist =
        "#EXTM3U\n"
        + "#EXT-X-TWITCH-INFO:NODE=\"video-edge\",SERVER-TIME=\"1.0\"\n"
        + "#EXT-X-MEDIA:TYPE=VIDEO,GROUP-ID=\"chunked\",NAME=\"1080p60 (source)\",AUTOSELECT=YES,DEFAULT=YES\n"
        + "#EXT-X-STREAM-INF:BANDWIDTH=6000000,RESOLUTION=1920x1080,VIDEO=\"chunked\",FRAME-RATE=60.000\n"
        + "https://video-weaver.ams03.hls.ttvnw.net/v1/playlist/Cq0FAbc.m3u8\n"
        + "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"audio_only\",NAME=\"audio_only\",URI=\"https://video-weaver.ams03.hls.ttvnw.net/v1/playlist/Audio1.m3u8\"\n";

    private const string VariantPlaylist =
        "#EXTM3U\n"
        + "#EXT-X-TARGETDURATION:6\n"
        + "#EXT-X-MEDIA-SEQUENCE:100\n"
        + "#EXT-X-TWITCH-PREFETCH:https://video-edge-1.ams03.abs.hls.ttvnw.net/v1/segment/p1.ts\n"
        + "#EXTINF:2.000,live\n"
        + "https://video-edge-1.ams03.abs.hls.ttvnw.net/v1/segment/s100.ts\n"
        + "#EXTINF:2.000,live\n"
        + "s101.ts\n";

    [Fact]
    public async Task A_master_playlist_comes_back_with_every_nested_playlist_through_the_proxy()
    {
        StubTwitch twitch = new(HttpStatusCode.OK, MasterPlaylist);
        StreamPlaylistController controller = Controller(twitch);

        IActionResult result = await controller.Playlist(UsherEncoded, CancellationToken.None);

        ContentResult content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("application/vnd.apple.mpegurl");
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        twitch.Requests.Should().ContainSingle().Which.Should().Be(UsherAddress);
        content
            .Content!.Split('\n')
            .Should()
            .Equal(
                "#EXTM3U",
                "#EXT-X-TWITCH-INFO:NODE=\"video-edge\",SERVER-TIME=\"1.0\"",
                "#EXT-X-MEDIA:TYPE=VIDEO,GROUP-ID=\"chunked\",NAME=\"1080p60 (source)\",AUTOSELECT=YES,DEFAULT=YES",
                "#EXT-X-STREAM-INF:BANDWIDTH=6000000,RESOLUTION=1920x1080,VIDEO=\"chunked\",FRAME-RATE=60.000",
                $"../{VariantEncoded}/index.m3u8",
                $"#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"audio_only\",NAME=\"audio_only\",URI=\"../{AudioEncoded}/index.m3u8\"",
                ""
            );
    }

    [Fact]
    public async Task A_variant_playlist_keeps_its_segments_absolute_on_twitchs_cdn()
    {
        StubTwitch twitch = new(HttpStatusCode.OK, VariantPlaylist);

        IActionResult result = await Controller(twitch)
            .Playlist(VariantEncoded, CancellationToken.None);

        ContentResult content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("application/vnd.apple.mpegurl");
        content
            .Content!.Split('\n')
            .Should()
            .Equal(
                "#EXTM3U",
                "#EXT-X-TARGETDURATION:6",
                "#EXT-X-MEDIA-SEQUENCE:100",
                "#EXT-X-TWITCH-PREFETCH:https://video-edge-1.ams03.abs.hls.ttvnw.net/v1/segment/p1.ts",
                "#EXTINF:2.000,live",
                "https://video-edge-1.ams03.abs.hls.ttvnw.net/v1/segment/s100.ts",
                "#EXTINF:2.000,live",
                "https://video-weaver.ams03.hls.ttvnw.net/v1/playlist/s101.ts",
                ""
            );
    }

    [Fact]
    public async Task No_cookie_token_or_header_of_ours_reaches_twitch()
    {
        StubTwitch twitch = new(HttpStatusCode.OK, MasterPlaylist);
        StreamPlaylistController controller = Controller(twitch);
        controller.Request.Headers.Cookie = "nnz_refresh=secret";
        controller.Request.Headers.Authorization = "Bearer secret";

        await controller.Playlist(UsherEncoded, CancellationToken.None);

        twitch.Headers.Should().ContainSingle().Which.Should().BeEmpty();
    }

    [Theory]
    [InlineData("aHR0cDovL3VzaGVyLnR0dm53Lm5ldC9hcGkvY2hhbm5lbC9obHMvYS5tM3U4")] // http://usher.ttvnw.net/...
    [InlineData("aHR0cHM6Ly9ldmlsLmV4YW1wbGUvYS5tM3U4")] // https://evil.example/a.m3u8
    [InlineData("aHR0cHM6Ly90dHZudy5uZXQuZXZpbC5leGFtcGxlL2EubTN1OA")] // https://ttvnw.net.evil.example/a.m3u8
    [InlineData("aHR0cHM6Ly91c2hlci50dHZudy5uZXRAZXZpbC5leGFtcGxlL2EubTN1OA")] // https://usher.ttvnw.net@evil.example/a.m3u8
    [InlineData("aHR0cHM6Ly91c2VyQGV2aWwuZXhhbXBsZS9hLm0zdTg")] // https://user@evil.example/a.m3u8
    [InlineData("aHR0cHM6Ly91c2VyOnB3QHVzaGVyLnR0dm53Lm5ldC9hLm0zdTg")] // https://user:pw@usher.ttvnw.net/a.m3u8
    [InlineData("aHR0cHM6Ly91c2hlci50dHZudy5uZXQ6ODQ0My9hLm0zdTg")] // https://usher.ttvnw.net:8443/a.m3u8
    [InlineData("aHR0cHM6Ly90dHZudy5uZXQvYS5tM3U4")] // https://ttvnw.net/a.m3u8
    [InlineData("aHR0cHM6Ly9ldmlsdHR2bncubmV0L2EubTN1OA")] // https://evilttvnw.net/a.m3u8
    [InlineData("ZmlsZTovLy9ldGMvcGFzc3dk")] // file:///etc/passwd
    [InlineData("not base64!")]
    [InlineData("")]
    public async Task An_address_off_the_twitch_allowlist_is_refused_and_never_fetched(
        string encoded
    )
    {
        StubTwitch twitch = new(HttpStatusCode.OK, MasterPlaylist);
        StreamPlaylistController controller = Controller(twitch);

        IActionResult result = await controller.Playlist(encoded, CancellationToken.None);

        result
            .Should()
            .BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should()
            .Be(StatusCodes.Status400BadRequest);
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        twitch.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task An_offline_channel_answers_not_found()
    {
        StubTwitch twitch = new(HttpStatusCode.NotFound, "[]");

        IActionResult result = await Controller(twitch)
            .Playlist(UsherEncoded, CancellationToken.None);

        result
            .Should()
            .BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should()
            .Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task A_redirect_from_upstream_is_never_followed_so_it_cannot_leave_the_allowlist()
    {
        StubTwitch twitch = new(HttpStatusCode.Found, "", "https://evil.example/a.m3u8");

        IActionResult result = await Controller(twitch)
            .Playlist(UsherEncoded, CancellationToken.None);

        result
            .Should()
            .BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should()
            .Be(StatusCodes.Status502BadGateway);
        twitch.Requests.Should().ContainSingle().Which.Should().Be(UsherAddress);
    }

    [Fact]
    public async Task The_rate_limit_lets_four_watchers_refresh_every_two_seconds_from_one_ip_then_rejects()
    {
        using PartitionedRateLimiter<HttpContext> limiter = PartitionedRateLimiter.Create<
            HttpContext,
            string
        >(LivePlaylistRateLimitPolicy.Partition);

        List<bool> acquired = [];
        for (int i = 0; i <= LivePlaylistRateLimitPolicy.PermitLimit; i++)
        {
            DefaultHttpContext context = new();
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
            using RateLimitLease lease = await limiter.AcquireAsync(context);
            acquired.Add(lease.IsAcquired);
        }

        // Four watchers at one refresh per 2 s is 120 a minute.
        LivePlaylistRateLimitPolicy.PermitLimit.Should().BeGreaterThanOrEqualTo(120);
        LivePlaylistRateLimitPolicy.Window.Should().Be(TimeSpan.FromMinutes(1));
        acquired
            .Take(LivePlaylistRateLimitPolicy.PermitLimit)
            .Should()
            .AllSatisfy(x => x.Should().BeTrue());
        acquired.Last().Should().BeFalse();
    }

    private static StreamPlaylistController Controller(StubTwitch twitch)
    {
        TwitchLivePlaylistProxy proxy = new(new StubClientFactory(twitch));
        return new(proxy) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
    }

    /// <summary>Twitch's side: answers every request with one status and body, and records what it was sent.</summary>
    private sealed class StubTwitch(HttpStatusCode status, string body, string? location = null)
        : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public List<List<string>> Headers { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            Headers.Add([.. request.Headers.Select(header => header.Key)]);
            HttpResponseMessage response = new(status) { Content = new StringContent(body) };
            if (location is not null)
                response.Headers.Location = new(location);

            return Task.FromResult(response);
        }
    }

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
