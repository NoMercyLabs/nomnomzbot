// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves <see cref="RewardRedeemedBroadcastHandler"/> carries the GAP E3-2 hub-broadcast-layer enrichment
/// additively on <see cref="RewardRedeemedDto"/> — populated when the enricher has data, and <c>null</c> (never
/// a crash) when it doesn't — fans the SAME decorated dto to the overlays as a "reward_redeemed" event — and
/// (S058b) plays a subscribed widget's configured alert sound on the shared overlay audio bus, or stays silent
/// without error when none is configured.
/// </summary>
public sealed class RewardRedeemedBroadcastHandlerTests
{
    private static RewardRedeemedEvent Event(Guid channel) =>
        new()
        {
            BroadcasterId = channel,
            RewardId = "r1",
            RewardTitle = "Hydrate",
            RedemptionId = "red1",
            UserId = "u1",
            UserDisplayName = "Stoney",
            Cost = 100,
        };

    [Fact]
    public async Task Redemption_with_known_viewer_carries_the_enriched_fields()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        ISoundClipService soundClips = Substitute.For<ISoundClipService>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "u1", Arg.Any<CancellationToken>())
            .Returns(new HubUserEnrichment("Stoney", "https://cdn/avatar.png", "she/her", "Vip"));
        RewardRedeemedBroadcastHandler handler = new(notifier, enricher, db, widgets, soundClips);

        await handler.HandleAsync(Event(channel));

        await notifier
            .Received(1)
            .SendRewardRedeemedAsync(
                channel.ToString(),
                Arg.Is<RewardRedeemedDto>(dto =>
                    dto.AvatarUrl == "https://cdn/avatar.png"
                    && dto.Pronouns == "she/her"
                    && dto.CommunityStanding == "Vip"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Redemption_with_unknown_viewer_carries_null_enrichment_not_a_crash()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        ISoundClipService soundClips = Substitute.For<ISoundClipService>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "u1", Arg.Any<CancellationToken>())
            .Returns((HubUserEnrichment?)null);
        RewardRedeemedBroadcastHandler handler = new(notifier, enricher, db, widgets, soundClips);

        await handler.HandleAsync(Event(channel));

        await notifier
            .Received(1)
            .SendRewardRedeemedAsync(
                channel.ToString(),
                Arg.Is<RewardRedeemedDto>(dto =>
                    dto.AvatarUrl == null && dto.Pronouns == null && dto.CommunityStanding == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Redemption_is_also_pushed_to_overlays_as_a_decorated_reward_redeemed_event()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        ISoundClipService soundClips = Substitute.For<ISoundClipService>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "u1", Arg.Any<CancellationToken>())
            .Returns(new HubUserEnrichment("Stoney", "https://cdn/avatar.png", "she/her", "Vip"));
        Widget widget = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Redemption alert",
            IsEnabled = true,
            EventSubscriptions = ["reward_redeemed"],
        };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        RewardRedeemedBroadcastHandler handler = new(notifier, enricher, db, widgets, soundClips);

        await handler.HandleAsync(Event(channel));

        await widgets
            .Received(1)
            .BroadcastOverlayEventAsync(
                channel.ToString(),
                Arg.Is<OverlayEventDto>(evt =>
                    evt.Type == "reward_redeemed"
                    && evt.Payload.Contains("\"avatarUrl\":\"https://cdn/avatar.png\"")
                    && evt.Payload.Contains("\"communityStanding\":\"Vip\"")
                    && evt.Payload.Contains("\"rewardTitle\":\"Hydrate\"")
                ),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                widget.Id.ToString(),
                Arg.Is<WidgetEventDto>(evt =>
                    evt.EventType == "reward_redeemed"
                    && evt.Data is RewardRedeemedDto
                    && ((RewardRedeemedDto)evt.Data!).CommunityStanding == "Vip"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Redemption_with_no_sound_configured_stays_silent_without_error()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        ISoundClipService soundClips = Substitute.For<ISoundClipService>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "u1", Arg.Any<CancellationToken>())
            .Returns((HubUserEnrichment?)null);
        Widget widget = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Redemption alert",
            IsEnabled = true,
            EventSubscriptions = ["reward_redeemed"],
            Settings = new() { ["soundClipId"] = "" },
        };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        RewardRedeemedBroadcastHandler handler = new(notifier, enricher, db, widgets, soundClips);

        await handler.HandleAsync(Event(channel));

        await soundClips
            .DidNotReceive()
            .ResolveForPlaybackAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .DidNotReceive()
            .PlaySoundAsync(
                Arg.Any<string>(),
                Arg.Any<PlaySoundPayload>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Redemption_with_a_configured_sound_pushes_the_resolved_clip_to_the_overlay_audio_bus()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        ISoundClipService soundClips = Substitute.For<ISoundClipService>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "u1", Arg.Any<CancellationToken>())
            .Returns((HubUserEnrichment?)null);
        Widget widget = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Redemption alert",
            IsEnabled = true,
            EventSubscriptions = ["reward_redeemed"],
            Settings = new() { ["soundClipId"] = "hydrate-ding" },
        };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();
        Guid clipId = Guid.CreateVersion7();
        soundClips
            .ResolveForPlaybackAsync(channel, "hydrate-ding", null, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new SoundPlaybackDto(clipId, "/sounds/clip.mp3?token=abc", 80, 1500))
            );

        RewardRedeemedBroadcastHandler handler = new(notifier, enricher, db, widgets, soundClips);

        await handler.HandleAsync(Event(channel));

        await widgets
            .Received(1)
            .PlaySoundAsync(
                channel.ToString(),
                Arg.Is<PlaySoundPayload>(p =>
                    p.PlaybackUrl == "/sounds/clip.mp3?token=abc"
                    && p.Volume == 80
                    && p.Handle == null
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
