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

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Discord.EventHandlers;

/// <summary>
/// The <c>stream_end</c> Discord trigger: posts the channel's rule when the stream goes offline. The key is
/// the stream's go-live instant when the offline signal carries it, so a repeated signal dedupes and a later
/// stream posts again. When the go-live instant is unknown the key falls back to the minute of arrival.
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

    // The stream's own go-live instant names it, so a repeated offline signal dedupes whenever it arrives.
    // Without that instant the minute of arrival is the best available identity.
    protected override string DedupeKeyOf(ChannelOfflineEvent @event) =>
        @event.StreamStartedAt is { } startedAt
            ? $"{Trigger}:{@event.Provider}:{startedAt.UtcDateTime:O}"
            : $"{Trigger}:{@event.Provider}:{@event.OccurredAt.UtcDateTime:yyyyMMddHHmm}";

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
