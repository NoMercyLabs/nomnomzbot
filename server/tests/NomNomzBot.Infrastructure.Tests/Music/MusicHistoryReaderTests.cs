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

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using RequestRecord = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the shape the Music page History and "Thanks for the music" panels read back from stored
/// <c>SongRequest</c> rows: order, requester name, tenant isolation and counts.
/// </summary>
public sealed class MusicHistoryReaderTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000cc01");
    private static readonly Guid Other = Guid.Parse("0192a000-0000-7000-8000-00000000cc02");
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Recent_lists_requests_newest_first_with_the_requester_display_name_and_track_shape()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        AddUser(db, "100", "Alice");
        AddUser(db, "200", "Bob");
        AddRow(db, Channel, "100", "spotify:track:a", "Song A", "Artist A", T0);
        AddRow(db, Channel, "200", "spotify:track:b", "Song B", "Artist B", T0.AddMinutes(5));
        AddRow(db, Other, "100", "spotify:track:x", "Elsewhere", "Nobody", T0.AddMinutes(9));
        db.SaveChanges();
        MusicHistoryReader sut = new(db);

        Result<List<SongHistoryItemDto>> result = await sut.GetRecentAsync(Channel, 10);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(i => i.TrackName).Should().Equal("Song B", "Song A");
        SongHistoryItemDto first = result.Value[0];
        first.Artist.Should().Be("Artist B");
        first.Provider.Should().Be("spotify");
        first.RequesterId.Should().Be("200");
        first.RequesterName.Should().Be("Bob");
        first.RequestedAt.Should().Be(T0.AddMinutes(5));
    }

    [Fact]
    public async Task Recent_and_top_requesters_carry_the_requester_chat_color()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        AddUser(db, "100", "Alice", "#FF69B4");
        AddUser(db, "200", "Bob");
        AddRow(db, Channel, "100", "spotify:track:a", "Song A", "Artist A", T0);
        AddRow(db, Channel, "200", "spotify:track:b", "Song B", "Artist B", T0.AddMinutes(5));
        db.SaveChanges();
        MusicHistoryReader sut = new(db);

        Result<List<SongHistoryItemDto>> recent = await sut.GetRecentAsync(Channel, 10);
        Result<List<TopRequesterDto>> top = await sut.GetTopRequestersAsync(Channel, 10);

        recent.Value.Single(i => i.RequesterId == "100").RequesterColor.Should().Be("#FF69B4");
        recent.Value.Single(i => i.RequesterId == "200").RequesterColor.Should().BeNull();
        top.Value.Single(t => t.UserId == "100").Color.Should().Be("#FF69B4");
        top.Value.Single(t => t.UserId == "200").Color.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_requesters_maps_a_display_name_to_the_twitch_id_and_color_ignoring_case()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        AddUser(db, "100", "Alice", "#FF69B4");
        db.SaveChanges();
        MusicHistoryReader sut = new(db);

        Dictionary<string, RequesterIdentityDto> found = await sut.ResolveRequestersAsync([
            "alice",
            "Spotify",
        ]);

        found.Should().ContainSingle();
        found["alice"].Should().Be(new RequesterIdentityDto("100", "#FF69B4"));
    }

    [Fact]
    public async Task Recent_honors_the_limit()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        for (int i = 0; i < 5; i++)
            AddRow(db, Channel, "100", "spotify:track:" + i, "S" + i, "A", T0.AddMinutes(i));
        db.SaveChanges();

        Result<List<SongHistoryItemDto>> result = await new MusicHistoryReader(db).GetRecentAsync(
            Channel,
            2
        );

        result.Value.Select(i => i.TrackName).Should().Equal("S4", "S3");
    }

    [Fact]
    public async Task Top_requesters_counts_rows_per_viewer_highest_first_within_this_channel_only()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        AddUser(db, "100", "Alice");
        AddUser(db, "200", "Bob");
        AddRow(db, Channel, "200", "spotify:track:a", "A", "X", T0);
        AddRow(db, Channel, "200", "spotify:track:a", "A", "X", T0.AddMinutes(1));
        AddRow(db, Channel, "200", "spotify:track:b", "B", "X", T0.AddMinutes(2));
        AddRow(db, Channel, "100", "spotify:track:c", "C", "X", T0.AddMinutes(3));
        AddRow(db, Other, "100", "spotify:track:d", "D", "X", T0.AddMinutes(4));
        AddRow(db, Other, "100", "spotify:track:e", "E", "X", T0.AddMinutes(5));
        db.SaveChanges();

        Result<List<TopRequesterDto>> result = await new MusicHistoryReader(
            db
        ).GetTopRequestersAsync(Channel, 10);

        result.Value.Should().HaveCount(2);
        result.Value[0].Should().Be(new TopRequesterDto("200", "Bob", 3));
        result.Value[1].Should().Be(new TopRequesterDto("100", "Alice", 1));
    }

    private static void AddUser(
        MusicTestDbContext db,
        string twitchId,
        string name,
        string? color = null
    ) =>
        db.Users.Add(
            new User
            {
                TwitchUserId = twitchId,
                Username = name.ToLowerInvariant(),
                UsernameNormalized = name.ToLowerInvariant(),
                DisplayName = name,
                Color = color,
            }
        );

    private static void AddRow(
        MusicTestDbContext db,
        Guid channel,
        string userId,
        string uri,
        string name,
        string artist,
        DateTime at
    ) =>
        db.Records.Add(
            new RequestRecord
            {
                BroadcasterId = channel,
                UserId = userId,
                RecordType = SongRequestHistory.RecordType,
                CreatedAt = at,
                Data = JsonSerializer.Serialize(
                    new SongRequestHistory(uri, name, artist, null, "spotify"),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
                ),
            }
        );
}
