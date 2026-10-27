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
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

public sealed class SongSearchRankerTests
{
    [Fact]
    public void A_version_the_query_asks_for_is_not_pushed_down()
    {
        TrackInfo pick = Pick(
            "bohemian rhapsody live",
            Hit("live", "Bohemian Rhapsody (Live)", "Queen"),
            Hit("studio", "Bohemian Rhapsody", "Queen")
        );

        pick.ProviderTrackId.Should().Be("live");
    }

    [Fact]
    public void A_version_the_query_did_not_ask_for_goes_below_the_plain_song()
    {
        TrackInfo pick = Pick(
            "bohemian rhapsody",
            Hit("live", "Bohemian Rhapsody (Live)", "Queen"),
            Hit("studio", "Bohemian Rhapsody", "Queen")
        );

        pick.ProviderTrackId.Should().Be("studio");
    }

    [Fact]
    public void Stop_words_alone_do_not_make_a_hit_relevant()
    {
        TrackInfo pick = Pick(
            "love in the sky",
            Hit("noise", "In The Mood", "Glenn Miller"),
            Hit("real", "Love In The Sky", "Somebody")
        );

        pick.ProviderTrackId.Should().Be("real");
    }

    [Fact]
    public void A_title_by_artist_query_prefers_the_hit_by_that_artist()
    {
        TrackInfo pick = Pick(
            "wonderwall by oasis",
            Hit("cover", "Wonderwall", "Ryan Adams"),
            Hit("real", "Wonderwall", "Oasis")
        );

        pick.ProviderTrackId.Should().Be("real");
    }

    [Fact]
    public void When_no_hit_shares_a_title_word_nothing_is_picked()
    {
        SongSearchRanker
            .Pick("zzzz yyyy", [Hit("first", "Alpha", "One"), Hit("second", "Beta", "Two")])
            .Should()
            .BeNull();
    }

    [Fact]
    public void A_hit_that_only_shares_the_artist_is_not_the_requested_song()
    {
        SongSearchRanker
            .Pick(
                "barbie girl cover - alex terrible",
                [Hit("doom", "Doom Slayer", "Alex Terrible")]
            )
            .Should()
            .BeNull();
    }

    [Fact]
    public void The_title_match_wins_over_the_artists_other_song_listed_first()
    {
        TrackInfo pick = Pick(
            "barbie girl cover - alex terrible",
            Hit("doom", "Doom Slayer", "Alex Terrible"),
            Hit("barbie", "Barbie Girl (cover)", "Cover Band")
        );

        pick.ProviderTrackId.Should().Be("barbie");
    }

    [Fact]
    public void A_request_that_is_only_the_artist_takes_that_artists_first_song()
    {
        TrackInfo pick = Pick(
            "oasis",
            Hit("other", "Some Song", "Someone Else"),
            Hit("oasis", "Wonderwall", "Oasis")
        );

        pick.ProviderTrackId.Should().Be("oasis");
    }

    [Fact]
    public void A_typo_in_a_long_word_still_matches_the_title()
    {
        TrackInfo pick = Pick(
            "bohemian rapsody",
            Hit("noise", "Rapid Fire", "Judas Priest"),
            Hit("song", "Bohemian Rhapsody", "Queen")
        );

        pick.ProviderTrackId.Should().Be("song");
    }

    [Fact]
    public void The_closest_hit_is_the_one_whose_title_carries_most_of_the_request()
    {
        TrackInfo? closest = SongSearchRanker.Closest(
            "barbie girl cover - alex terrible",
            [
                Hit("doom", "Doom Slayer", "Alex Terrible"),
                Hit("doll", "Barbie Doll", "Toy Band"),
                Hit("world", "Barbie World", "Nicki Minaj"),
            ]
        );

        closest!.ProviderTrackId.Should().Be("doll");
    }

    [Fact]
    public void Nothing_is_close_when_no_title_shares_a_word()
    {
        SongSearchRanker
            .Closest("barbie girl", [Hit("doom", "Doom Slayer", "Alex Terrible")])
            .Should()
            .BeNull();
    }

    [Theory]
    [InlineData("Never Gonna Give You Up REACTION")]
    [InlineData("First Time Hearing - Never Gonna Give You Up (React)")]
    [InlineData("Never Gonna Give You Up 10 Hours")]
    [InlineData("Never Gonna Give You Up [1 hour loop]")]
    [InlineData("Never Gonna Give You Up (Loop)")]
    public void A_reaction_or_loop_upload_goes_below_the_song_itself(string filler)
    {
        TrackInfo pick = Pick(
            "never gonna give you up",
            Hit("filler", filler, "Some Channel"),
            Hit("song", "Never Gonna Give You Up", "Some Channel")
        );

        pick.ProviderTrackId.Should().Be("song");
    }

    [Fact]
    public void A_reaction_the_query_asks_for_is_not_pushed_down()
    {
        TrackInfo pick = Pick(
            "never gonna give you up reaction",
            Hit("reaction", "Never Gonna Give You Up REACTION", "Reactor"),
            Hit("song", "Never Gonna Give You Up", "Some Channel")
        );

        pick.ProviderTrackId.Should().Be("reaction");
    }

    [Theory]
    [InlineData("Never Gonna Give You Up", "Rick Astley - Topic")]
    [InlineData("Never Gonna Give You Up", "RickAstleyVEVO")]
    [InlineData("Never Gonna Give You Up (Official Music Video)", "Some Uploader")]
    public void An_official_upload_comes_before_a_fan_upload(string title, string channel)
    {
        TrackInfo pick = Pick(
            "never gonna give you up",
            Hit("fan", "Never Gonna Give You Up", "Fan Uploads"),
            Hit("official", title, channel)
        );

        pick.ProviderTrackId.Should().Be("official");
    }

    [Fact]
    public void No_hits_pick_nothing()
    {
        SongSearchRanker.Pick("anything", []).Should().BeNull();
    }

    private static TrackInfo Pick(string query, params TrackInfo[] hits) =>
        SongSearchRanker.Pick(query, hits)!;

    private static TrackInfo Hit(string id, string name, string artist) =>
        new()
        {
            TrackName = name,
            Artist = artist,
            Album = "Album",
            TrackUri = $"spotify:track:{id}",
            Provider = "spotify",
            ProviderTrackId = id,
        };
}
