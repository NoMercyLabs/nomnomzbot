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

using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

public sealed class YouTubeVideoIdTests
{
    private const string Id = "dQw4w9WgXcQ";

    [Theory]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?t=42")]
    [InlineData("https://youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/v/dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/e/dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/live/dQw4w9WgXcQ?feature=share")]
    [InlineData("https://youtube.com/shorts/dQw4w9WgXcQ?feature=share")]
    [InlineData("https://youtube.com/watch?feature=share&v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch/dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?app=desktop&v=dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ&si=x")]
    [InlineData("https://gaming.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("http://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("http://youtu.be/dQw4w9WgXcQ")]
    [InlineData("www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://WWW.YOUTUBE.COM/watch?v=dQw4w9WgXcQ")]
    [InlineData(
        "https://youtube.com/attribution_link?u=%2Fwatch%3Fv%3DdQw4w9WgXcQ%26feature%3Dshare"
    )]
    [InlineData("(https://youtu.be/dQw4w9WgXcQ)")]
    [InlineData("<https://youtu.be/dQw4w9WgXcQ>")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ,")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ.")]
    [InlineData("\"https://youtu.be/dQw4w9WgXcQ\"")]
    [InlineData("dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ#t=30")]
    public void Extract_every_link_shape_yields_the_video_id(string input)
    {
        Assert.Equal(Id, YouTubeVideoId.Extract(input));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=QMQXikJWrCA")]
    [InlineData("https://www.youtube.com/shorts/QMQXikJWrCA")]
    public void Extract_a_short_opened_either_way_yields_its_id(string input)
    {
        Assert.Equal("QMQXikJWrCA", YouTubeVideoId.Extract(input));
    }

    [Theory]
    [InlineData("https://www.youtube.com/channel/UCuAXFkgsw1L7xaCfnd5JJOw")]
    [InlineData("https://www.youtube.com/@SomeHandle")]
    [InlineData("https://www.youtube.com/playlist?list=PLabcdefghijk")]
    [InlineData("https://www.youtube.com/feed/trending")]
    [InlineData("https://vimeo.com/dQw4w9WgXcQ")]
    [InlineData("https://notyoutube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/short")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQx")]
    [InlineData("dQw4w9WgXc")]
    public void Extract_refuses_what_is_not_one_video(string input)
    {
        Assert.Null(YouTubeVideoId.Extract(input));
    }
}
