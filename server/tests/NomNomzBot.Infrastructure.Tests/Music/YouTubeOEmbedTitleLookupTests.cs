// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>The keyless oEmbed title read: right endpoint, right link shapes, every failure reads as no title.</summary>
public sealed class YouTubeOEmbedTitleLookupTests
{
    private const string Link =
        "https://www.youtube.com/watch?v=Kg8NeUI-aQQ&list=RDKg8NeUI-aQQ&start_radio=1";

    [Fact]
    public async Task A_youtube_link_returns_the_oembed_title_from_the_encoded_oembed_url()
    {
        RecordingHttpHandler handler = new();
        handler.RespondWhen(
            _ => true,
            HttpStatusCode.OK,
            "{\"title\":\"Zanzibar - Hakuna Matata\",\"author_name\":\"x\"}"
        );

        string? title = await Lookup(handler).TryGetTitleAsync(Link, CancellationToken.None);

        title.Should().Be("Zanzibar - Hakuna Matata");
        handler
            .RequestUrls.Should()
            .ContainSingle()
            .Which.Should()
            .Be($"GET https://www.youtube.com/oembed?url={Uri.EscapeDataString(Link)}&format=json");
    }

    [Theory]
    [InlineData("https://youtu.be/Kg8NeUI-aQQ")]
    [InlineData("https://music.youtube.com/watch?v=Kg8NeUI-aQQ")]
    [InlineData("https://www.youtube.com/shorts/Kg8NeUI-aQQ")]
    public async Task Every_youtube_link_shape_is_looked_up(string link)
    {
        RecordingHttpHandler handler = new();
        handler.RespondWhen(_ => true, HttpStatusCode.OK, "{\"title\":\"A Title\"}");

        string? title = await Lookup(handler).TryGetTitleAsync(link, CancellationToken.None);

        title.Should().Be("A Title");
    }

    [Theory]
    [InlineData("https://soundcloud.com/a/b")]
    [InlineData("https://www.youtube.com/channel/UC123")]
    [InlineData("never gonna give you up")]
    public async Task A_link_that_is_not_a_youtube_video_makes_no_call_and_has_no_title(string link)
    {
        RecordingHttpHandler handler = new();

        string? title = await Lookup(handler).TryGetTitleAsync(link, CancellationToken.None);

        title.Should().BeNull();
        handler.RequestUrls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{}")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, "{\"title\":\"  \"}")]
    [InlineData(HttpStatusCode.OK, "{\"other\":1}")]
    public async Task Any_failure_reads_as_no_title(HttpStatusCode status, string body)
    {
        RecordingHttpHandler handler = new();
        handler.RespondWhen(_ => true, status, body);

        string? title = await Lookup(handler).TryGetTitleAsync(Link, CancellationToken.None);

        title.Should().BeNull();
    }

    private static YouTubeOEmbedTitleLookup Lookup(HttpMessageHandler handler) =>
        new(new SingleHandlerClientFactory(handler));
}
