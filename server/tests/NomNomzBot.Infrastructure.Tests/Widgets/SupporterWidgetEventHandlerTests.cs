// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Supporters.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Widgets.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the supporter-event -> overlay routing gap (S058b) is closed: a real, backend-published
/// <see cref="SupporterEventReceived"/> reaches every enabled widget subscribed to its
/// <c>supporter.&lt;kind&gt;</c> event type, over the real <see cref="IWidgetEventNotifier"/> seam — with the
/// exact widget id, event type, and payload shape (<see cref="SupporterAlertPayload"/>) that
/// <c>alerts.vue</c>/<c>event_ticker.vue</c> now read, not merely "the notifier was called".
/// </summary>
public sealed class SupporterWidgetEventHandlerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000000d1");
    private static readonly Guid OtherBroadcaster = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000d2"
    );

    private readonly IWidgetEventNotifier _overlay = Substitute.For<IWidgetEventNotifier>();

    private static Widget NewWidget(
        Guid broadcasterId,
        bool enabled,
        params string[] subscriptions
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcasterId,
            Name = "test-widget",
            IsEnabled = enabled,
            EventSubscriptions = [.. subscriptions],
        };

    private static async Task SeedChannelAsync(WidgetSqliteTestDatabase database, Guid channelId)
    {
        await using WidgetTestDbContext db = database.NewContext();
        db.Channels.Add(
            new()
            {
                Id = channelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = channelId.ToString("N")[..12],
                Name = "teststreamer",
                NameNormalized = "teststreamer",
                OverlayToken = channelId.ToString("N"),
            }
        );
        await db.SaveChangesAsync();
    }

    private static SupporterEventReceived TipEvent(Guid broadcasterId) =>
        new()
        {
            BroadcasterId = broadcasterId,
            OccurredAt = DateTimeOffset.UtcNow,
            SourceKey = "kofi",
            Kind = "tip",
            SupporterDisplayName = "GenerousGoat",
            AmountMinor = 500,
            Currency = "USD",
            MessageText = "keep it up!",
            SupporterEventId = Guid.CreateVersion7(),
        };

    [Fact]
    public async Task Tip_event_pushes_the_real_payload_to_every_widget_subscribed_to_supporter_tip()
    {
        using WidgetSqliteTestDatabase db = WidgetSqliteTestDatabase.Open();
        await SeedChannelAsync(db, Broadcaster);
        await SeedChannelAsync(db, OtherBroadcaster);
        Widget alerts = NewWidget(Broadcaster, true, "supporter.tip", "follow");
        Widget ticker = NewWidget(Broadcaster, true, "supporter.tip");
        Widget notSubscribed = NewWidget(Broadcaster, true, "supporter.membership");
        Widget disabled = NewWidget(Broadcaster, false, "supporter.tip");
        Widget otherChannel = NewWidget(OtherBroadcaster, true, "supporter.tip");

        using (WidgetTestDbContext ctx = db.NewContext())
        {
            ctx.Widgets.AddRange(alerts, ticker, notSubscribed, disabled, otherChannel);
            await ctx.SaveChangesAsync();
        }

        using WidgetTestDbContext readCtx = db.NewContext();
        SupporterWidgetEventHandler handler = new(readCtx, _overlay);

        await handler.HandleAsync(TipEvent(Broadcaster));

        await _overlay
            .Received(1)
            .SendWidgetEventAsync(
                Broadcaster,
                alerts.Id,
                "supporter.tip",
                Arg.Is<object?>(p => IsExpectedTipPayload(p)),
                Arg.Any<CancellationToken>()
            );
        await _overlay
            .Received(1)
            .SendWidgetEventAsync(
                Broadcaster,
                ticker.Id,
                "supporter.tip",
                Arg.Is<object?>(p => IsExpectedTipPayload(p)),
                Arg.Any<CancellationToken>()
            );
        await _overlay
            .DidNotReceive()
            .SendWidgetEventAsync(
                Broadcaster,
                notSubscribed.Id,
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            );
        await _overlay
            .DidNotReceive()
            .SendWidgetEventAsync(
                Broadcaster,
                disabled.Id,
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            );
        await _overlay
            .DidNotReceive()
            .SendWidgetEventAsync(
                OtherBroadcaster,
                otherChannel.Id,
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("merch")]
    [InlineData("charity")]
    public async Task Every_supporter_kind_routes_to_its_own_supporter_dot_kind_event_type(
        string kind
    )
    {
        using WidgetSqliteTestDatabase db = WidgetSqliteTestDatabase.Open();
        await SeedChannelAsync(db, Broadcaster);
        Widget widget = NewWidget(Broadcaster, true, $"supporter.{kind}");
        using (WidgetTestDbContext ctx = db.NewContext())
        {
            ctx.Widgets.Add(widget);
            await ctx.SaveChangesAsync();
        }

        using WidgetTestDbContext readCtx = db.NewContext();
        SupporterWidgetEventHandler handler = new(readCtx, _overlay);

        await handler.HandleAsync(
            new SupporterEventReceived
            {
                BroadcasterId = Broadcaster,
                OccurredAt = DateTimeOffset.UtcNow,
                SourceKey = "patreon",
                Kind = kind,
                SupporterDisplayName = "Someone",
                Tier = null,
                SupporterEventId = Guid.CreateVersion7(),
            }
        );

        await _overlay
            .Received(1)
            .SendWidgetEventAsync(
                Broadcaster,
                widget.Id,
                $"supporter.{kind}",
                Arg.Is<object?>(p => IsExpectedKindPayload(p, kind)),
                Arg.Any<CancellationToken>()
            );
    }

    private static bool IsExpectedKindPayload(object? payload, string kind) =>
        payload is SupporterAlertPayload p && p.Kind == kind;

    private static bool IsExpectedTipPayload(object? payload) =>
        payload
            is SupporterAlertPayload
            {
                Kind: "tip",
                SupporterDisplayName: "GenerousGoat",
                AmountMinor: 500,
                Currency: "USD",
                MessageText: "keep it up!"
            };
}
