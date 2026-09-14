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
using NomNomzBot.Domain.Commands.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Widgets.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves <see cref="VoiceTriggerFiredEvent"/> reaches every enabled widget subscribed to the
/// <c>voice_trigger</c> event type over the real <see cref="IWidgetEventNotifier"/> seam — carrying the word,
/// the live count, and the sticker image URL the Alert widget's voice_trigger card reads — and never a widget
/// that is disabled, unsubscribed, or on a different channel.
/// </summary>
public sealed class VoiceTriggerWidgetEventHandlerTests
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

    [Fact]
    public async Task Fired_event_pushes_word_count_and_sticker_to_every_subscribed_widget()
    {
        using WidgetSqliteTestDatabase db = WidgetSqliteTestDatabase.Open();
        await SeedChannelAsync(db, Broadcaster);
        await SeedChannelAsync(db, OtherBroadcaster);
        Widget alert = NewWidget(Broadcaster, true, "voice_trigger");
        Widget notSubscribed = NewWidget(Broadcaster, true, "follow");
        Widget disabled = NewWidget(Broadcaster, false, "voice_trigger");
        Widget otherChannel = NewWidget(OtherBroadcaster, true, "voice_trigger");

        using (WidgetTestDbContext ctx = db.NewContext())
        {
            ctx.Widgets.AddRange(alert, notSubscribed, disabled, otherChannel);
            await ctx.SaveChangesAsync();
        }

        using WidgetTestDbContext readCtx = db.NewContext();
        VoiceTriggerWidgetEventHandler handler = new(readCtx, _overlay);

        VoiceTriggerFiredEvent fired = new()
        {
            BroadcasterId = Broadcaster,
            OccurredAt = DateTimeOffset.UtcNow,
            VoiceTriggerId = Guid.CreateVersion7(),
            Word = "technically",
            NewCount = 11,
            StickerImageUrl = "/api/v1/assets/file/" + Broadcaster + "/boo",
        };

        await handler.HandleAsync(fired);

        await _overlay
            .Received(1)
            .SendWidgetEventAsync(
                Broadcaster,
                alert.Id,
                "voice_trigger",
                Arg.Is<object?>(p =>
                    IsExpectedPayload(p, "technically", 11, fired.StickerImageUrl)
                ),
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

    [Fact]
    public async Task Platform_scoped_event_with_empty_broadcaster_pushes_nothing()
    {
        using WidgetSqliteTestDatabase db = WidgetSqliteTestDatabase.Open();
        using WidgetTestDbContext readCtx = db.NewContext();
        VoiceTriggerWidgetEventHandler handler = new(readCtx, _overlay);

        await handler.HandleAsync(
            new VoiceTriggerFiredEvent
            {
                BroadcasterId = Guid.Empty,
                OccurredAt = DateTimeOffset.UtcNow,
                VoiceTriggerId = Guid.CreateVersion7(),
                Word = "technically",
                NewCount = 1,
            }
        );

        await _overlay
            .DidNotReceiveWithAnyArgs()
            .SendWidgetEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            );
    }

    private static bool IsExpectedPayload(
        object? payload,
        string word,
        int count,
        string? stickerImageUrl
    ) =>
        payload is VoiceTriggerWidgetEventPayload p
        && p.Word == word
        && p.Count == count
        && p.StickerImageUrl == stickerImageUrl;
}
