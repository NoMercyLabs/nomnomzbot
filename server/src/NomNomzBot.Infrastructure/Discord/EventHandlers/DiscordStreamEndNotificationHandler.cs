// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Discord.EventHandlers;

/// <summary>
/// The <c>stream_end</c> Discord trigger: posts the channel's rule when the stream goes offline. The offline
/// signal carries no start time, so the key is the minute it arrived: a repeated signal dedupes, a later
/// stream posts again.
/// </summary>
public sealed class DiscordStreamEndNotificationHandler
    : DiscordTriggerNotificationHandler<ChannelOfflineEvent>
{
    private const string UnknownDuration = "unknown";

    public DiscordStreamEndNotificationHandler(
        IDiscordNotificationDispatcher dispatcher,
        IOutboundSanctionAccessor sanctions,
        ILogger<DiscordStreamEndNotificationHandler> logger
    )
        : base(dispatcher, sanctions, logger) { }

    protected override string Trigger => "stream_end";

    protected override string DedupeKeyOf(ChannelOfflineEvent @event) =>
        $"{Trigger}:{@event.Provider}:{@event.OccurredAt.UtcDateTime:yyyyMMddHHmm}";

    protected override Dictionary<string, string> TemplateDataOf(ChannelOfflineEvent @event) =>
        new()
        {
            ["broadcaster"] = @event.BroadcasterDisplayName,
            ["channel.name"] = @event.BroadcasterDisplayName,
            ["duration"] = DurationText(@event.StreamDuration),
        };

    // A zero duration means the go-live time was unknown, so the post must not claim the stream ran 0 minutes.
    private static string DurationText(TimeSpan duration) =>
        duration <= TimeSpan.Zero ? UnknownDuration
        : duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
        : $"{duration.Minutes}m";
}
