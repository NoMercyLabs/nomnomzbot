// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.Sound;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Sound;

/// <summary>
/// Regression coverage for the http-vs-https playback URL bug: behind the Cloudflare tunnel/reverse proxy every
/// request reaches Kestrel as plain http, so trusting <c>HttpContext.Request.Scheme</c> silently produced
/// <c>http://</c> TTS/sound-clip playback URLs even though the site is served over https — the overlay page's
/// CSP (<c>media-src 'self' https: ...</c>) then silently blocked all audio playback in the browser.
/// </summary>
public class DiskSoundClipStorePlaybackUrlTests
{
    [Fact]
    public async Task GetPlaybackUrlAsync_prefers_the_configured_App_BaseUrl_over_the_request_scheme()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["App:BaseUrl"] = "https://dev.nomnomz.bot" }
            )
            .Build();

        DefaultHttpContext httpContext = new();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("dev.nomnomz.bot");
        FakeHttpContextAccessor accessor = new(httpContext);

        DiskSoundClipStore store = new(accessor, configuration);

        Result<string> result = await store.GetPlaybackUrlAsync(
            "019f146e830371efb69818d1098d7d7e/clip.mp3"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith("https://dev.nomnomz.bot/api/v1/sound-clips/stream/");
    }

    [Fact]
    public async Task GetPlaybackUrlAsync_falls_back_to_the_request_scheme_when_App_BaseUrl_is_unset()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        DefaultHttpContext httpContext = new();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost:5080");
        FakeHttpContextAccessor accessor = new(httpContext);

        DiskSoundClipStore store = new(accessor, configuration);

        Result<string> result = await store.GetPlaybackUrlAsync(
            "019f146e830371efb69818d1098d7d7e/clip.mp3"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith("http://localhost:5080/api/v1/sound-clips/stream/");
    }

    private sealed class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public FakeHttpContextAccessor(HttpContext httpContext) => HttpContext = httpContext;

        public HttpContext? HttpContext { get; set; }
    }
}
