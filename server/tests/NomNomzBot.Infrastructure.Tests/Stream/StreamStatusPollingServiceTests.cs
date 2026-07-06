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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Stream.Jobs;

namespace NomNomzBot.Infrastructure.Tests.Stream;

/// <summary>
/// Proves the live-status reconcile (<see cref="StreamStatusPollingService.ApplyStreamState"/>) that fixes the
/// "live channel shows offline on the dashboard" bug: the poll writes the authoritative Helix Get Streams read into
/// both the in-memory <see cref="ChannelContext"/> (what the dashboard reads) and the persisted <see cref="Channel"/>,
/// treats an empty Helix result as offline, anchors the uptime clock only on the offline→live edge, and reports
/// whether a persisted field changed so the caller saves exactly once per cycle.
/// </summary>
public sealed class StreamStatusPollingServiceTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    private static Result<TwitchStream> Live(string title, string game) =>
        Result.Success(
            new TwitchStream(
                Id: "stream-1",
                UserId: "42",
                UserLogin: "aaoa_",
                UserName: "aaoa_",
                GameId: "509658",
                GameName: game,
                Type: "live",
                Title: title,
                Tags: [],
                ViewerCount: 7,
                StartedAt: StartedAt,
                Language: "en",
                ThumbnailUrl: "https://cdn/thumb",
                IsMature: false
            )
        );

    private static Result<TwitchStream> Offline() =>
        Result.Failure<TwitchStream>("channel is offline", "STREAM_OFFLINE");

    private static ChannelContext Ctx(bool isLive) =>
        new()
        {
            BroadcasterId = Guid.NewGuid(),
            TwitchChannelId = "42",
            ChannelName = "aaoa_",
            IsLive = isLive,
        };

    [Fact]
    public void Offline_to_live_sets_live_title_game_and_anchors_uptime()
    {
        ChannelContext ctx = Ctx(isLive: false);
        Channel dbChannel = new() { IsLive = false };

        bool changed = StreamStatusPollingService.ApplyStreamState(
            ctx,
            dbChannel,
            Live("Speedrun night", "Celeste")
        );

        changed.Should().BeTrue("the persisted IsLive flag flipped offline→live");
        ctx.IsLive.Should().BeTrue();
        ctx.CurrentTitle.Should().Be("Speedrun night");
        ctx.CurrentGame.Should().Be("Celeste");
        ctx.WentLiveAt.Should().Be(StartedAt, "the uptime clock anchors on the rising edge");
        dbChannel.IsLive.Should().BeTrue();
        dbChannel.Title.Should().Be("Speedrun night");
        dbChannel.GameName.Should().Be("Celeste");
    }

    [Fact]
    public void Live_to_offline_clears_live_and_uptime()
    {
        ChannelContext ctx = Ctx(isLive: true);
        ctx.WentLiveAt = StartedAt;
        ctx.CurrentTitle = "Speedrun night";
        Channel dbChannel = new() { IsLive = true, Title = "Speedrun night" };

        bool changed = StreamStatusPollingService.ApplyStreamState(ctx, dbChannel, Offline());

        changed.Should().BeTrue("the persisted IsLive flag flipped live→offline");
        ctx.IsLive.Should().BeFalse();
        ctx.WentLiveAt.Should().BeNull("uptime is meaningless once offline");
        dbChannel.IsLive.Should().BeFalse();
    }

    [Fact]
    public void Still_offline_is_a_no_op_change()
    {
        ChannelContext ctx = Ctx(isLive: false);
        Channel dbChannel = new() { IsLive = false };

        bool changed = StreamStatusPollingService.ApplyStreamState(ctx, dbChannel, Offline());

        changed.Should().BeFalse("nothing persisted changed — the caller must not save");
        ctx.IsLive.Should().BeFalse();
        dbChannel.IsLive.Should().BeFalse();
    }

    [Fact]
    public void Still_live_with_unchanged_metadata_reports_no_change_and_does_not_reset_uptime()
    {
        ChannelContext ctx = Ctx(isLive: true);
        ctx.WentLiveAt = StartedAt;
        Channel dbChannel = new()
        {
            IsLive = true,
            Title = "Speedrun night",
            GameName = "Celeste",
        };

        bool changed = StreamStatusPollingService.ApplyStreamState(
            ctx,
            dbChannel,
            Live("Speedrun night", "Celeste")
        );

        changed.Should().BeFalse("already live with the same title/game — no persisted change");
        ctx.IsLive.Should().BeTrue();
        ctx.WentLiveAt.Should()
            .Be(StartedAt, "uptime anchor is only set on the rising edge, not re-stamped");
    }

    [Fact]
    public void Title_change_while_already_live_is_a_persisted_change()
    {
        ChannelContext ctx = Ctx(isLive: true);
        Channel dbChannel = new()
        {
            IsLive = true,
            Title = "Old title",
            GameName = "Celeste",
        };

        bool changed = StreamStatusPollingService.ApplyStreamState(
            ctx,
            dbChannel,
            Live("New title", "Celeste")
        );

        changed.Should().BeTrue("the title changed, so the row must be saved");
        ctx.CurrentTitle.Should().Be("New title");
        dbChannel.Title.Should().Be("New title");
    }
}
