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
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the ban/timeout/unban broadcasters carry the GAP E3-2 hub-broadcast-layer enrichment additively on
/// <see cref="ModActionDto"/>, keyed off the MODERATED viewer (<c>TargetUserId</c>), not the moderator —
/// populated when the enricher has data, and <c>null</c> (never a crash) when it doesn't — AND fan the SAME
/// decorated dto to the overlays as a "ban"/"timeout"/"unban" event.
/// </summary>
public sealed class BanBroadcastHandlersTests
{
    [Fact]
    public async Task Ban_with_known_target_carries_the_enriched_fields()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "target1", Arg.Any<CancellationToken>())
            .Returns(
                new HubUserEnrichment("Naughty", "https://cdn/avatar.png", "he/him", "Everyone")
            );
        UserBannedBroadcastHandler handler = new(notifier, enricher, db, widgets);

        await handler.HandleAsync(
            new UserBannedEvent
            {
                BroadcasterId = channel,
                TargetUserId = "target1",
                TargetDisplayName = "Naughty",
                ModeratorUserId = "mod1",
                Reason = "spam",
            }
        );

        await notifier
            .Received(1)
            .SendModActionAsync(
                channel.ToString(),
                Arg.Is<ModActionDto>(dto =>
                    dto.Action == "ban"
                    && dto.TargetUserId == "target1"
                    && dto.TargetDisplayName == "Naughty"
                    && dto.TargetAvatarUrl == "https://cdn/avatar.png"
                    && dto.TargetPronouns == "he/him"
                    && dto.TargetCommunityStanding == "Everyone"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Timeout_with_unknown_target_carries_null_enrichment_not_a_crash()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "target2", Arg.Any<CancellationToken>())
            .Returns((HubUserEnrichment?)null);
        UserTimedOutBroadcastHandler handler = new(notifier, enricher, db, widgets);

        await handler.HandleAsync(
            new UserTimedOutEvent
            {
                BroadcasterId = channel,
                TargetUserId = "target2",
                TargetDisplayName = "Chatty",
                ModeratorUserId = "mod1",
                DurationSeconds = 600,
            }
        );

        await notifier
            .Received(1)
            .SendModActionAsync(
                channel.ToString(),
                Arg.Is<ModActionDto>(dto =>
                    dto.Action == "timeout"
                    && dto.DurationSeconds == 600
                    && dto.TargetDisplayName == null
                    && dto.TargetAvatarUrl == null
                    && dto.TargetPronouns == null
                    && dto.TargetCommunityStanding == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Unban_with_known_target_carries_the_enriched_fields()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "target1", Arg.Any<CancellationToken>())
            .Returns(new HubUserEnrichment("Reformed", null, "they/them", null));
        UserUnbannedBroadcastHandler handler = new(notifier, enricher, db, widgets);

        await handler.HandleAsync(
            new UserUnbannedEvent
            {
                BroadcasterId = channel,
                TargetUserId = "target1",
                ModeratorUserId = "mod1",
            }
        );

        await notifier
            .Received(1)
            .SendModActionAsync(
                channel.ToString(),
                Arg.Is<ModActionDto>(dto =>
                    dto.Action == "unban"
                    && dto.TargetPronouns == "they/them"
                    && dto.TargetAvatarUrl == null
                    && dto.TargetCommunityStanding == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Ban_is_also_pushed_to_overlays_as_a_decorated_ban_event()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "target1", Arg.Any<CancellationToken>())
            .Returns(
                new HubUserEnrichment("Naughty", "https://cdn/avatar.png", "he/him", "Everyone")
            );
        Widget widget = new()
        {
            Id = Guid.NewGuid().ToString(),
            BroadcasterId = channel,
            Name = "Mod log",
            IsEnabled = true,
            EventSubscriptions = ["ban"],
        };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        UserBannedBroadcastHandler handler = new(notifier, enricher, db, widgets);

        await handler.HandleAsync(
            new UserBannedEvent
            {
                BroadcasterId = channel,
                TargetUserId = "target1",
                TargetDisplayName = "Naughty",
                ModeratorUserId = "mod1",
                Reason = "spam",
            }
        );

        await widgets
            .Received(1)
            .BroadcastOverlayEventAsync(
                channel.ToString(),
                Arg.Is<OverlayEventDto>(evt =>
                    evt.Type == "ban"
                    && evt.Payload.Contains("\"action\":\"ban\"")
                    && evt.Payload.Contains("\"targetAvatarUrl\":\"https://cdn/avatar.png\"")
                    && evt.Payload.Contains("\"targetCommunityStanding\":\"Everyone\"")
                ),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                widget.Id,
                Arg.Is<WidgetEventDto>(evt =>
                    evt.EventType == "ban"
                    && evt.Data is ModActionDto
                    && ((ModActionDto)evt.Data!).TargetCommunityStanding == "Everyone"
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
