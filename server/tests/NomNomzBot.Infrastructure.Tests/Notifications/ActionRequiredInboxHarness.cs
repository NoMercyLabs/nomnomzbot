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

using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Notifications.Sources;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// Builds the inbox exactly as DI does: every production <see cref="IActionRequiredSource"/> over one database,
/// so a test proves the aggregated result the dashboard receives rather than one source in isolation.
/// </summary>
internal static class ActionRequiredInboxHarness
{
    public static List<IActionRequiredSource> Sources(
        IApplicationDbContext db,
        TimeProvider clock
    ) =>
        [
            new DeadIntegrationTokenSource(db),
            new HeldChatMessageSource(db),
            new ViewerReportSource(db),
            new NoAppealsSource(),
            new UnmanagedRewardSource(db),
            new TwitchGrantGapSource(db),
            new WidgetBuildFailureSource(db),
            new OutboundWebhookFailureSource(db),
            new LostSongRequestSource(db, clock),
            new HeatAutoTimeoutFailureSource(db, clock),
            new AutoModDeleteFailedSource(db, clock),
            new SharedBanNotAppliedSource(db, clock),
            new ChatFilterActionFailureSource(db, clock),
            new SecurityNoticeSource(db),
            new BotNotModeratorSource(
                new BotModeratorStatusReader(db, new ChannelTwitchBotResolver(db))
            ),
            new MassBanFailureSource(db),
        ];

    // The real unban source fails for a channel with no row, which most inbox tests do not seed. Those tests
    // are not about appeals, so this stands in with a healthy "no appeals" read.
    private sealed class NoAppealsSource : IActionRequiredSource
    {
        public string SourceKey => "unban_requests";

        public IReadOnlyCollection<string> KeyPrefixes { get; } = ["unban:"];

        public IReadOnlyCollection<string> InvalidatingEventTypes { get; } = [];

        public Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
            Guid channelId,
            IReadOnlySet<string> dismissedKeys,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success<List<ActionRequiredItemDto>>([]));

        public Task<Result<List<string>>> ResolveDismissalKeysAsync(
            Guid channelId,
            string itemId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success<List<string>>([itemId]));
    }

    public static ActionRequiredInboxService Create(
        IApplicationDbContext db,
        TimeProvider? clock = null,
        RecordingChangeNotifier? notifier = null
    ) =>
        new(
            Sources(db, clock ?? TimeProvider.System),
            db,
            clock ?? TimeProvider.System,
            notifier ?? new RecordingChangeNotifier(),
            NullLogger<ActionRequiredInboxService>.Instance
        );
}

/// <summary>Records every channel the inbox signalled as changed, in order.</summary>
internal sealed class RecordingChangeNotifier : IActionRequiredChangeNotifier
{
    public List<Guid> Signalled { get; } = [];

    public void NotifyChanged(Guid channelId) => Signalled.Add(channelId);
}
