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

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Pushes a chat poll change (open, vote tally change, close) to dashboard clients as <c>ChatPollChanged</c>.
/// The payload carries the full poll, so the chat-poll card redraws its bars with no API call.
/// </summary>
public sealed class ChatPollChangedBroadcastHandler : IEventHandler<ChatPollChangedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ChatPollChangedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(ChatPollChangedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "ChatPollChanged",
            new ChatPollChangedAlertDto(
                @event.PollId,
                @event.Change,
                @event.Question,
                @event.Status,
                @event.TotalVotes,
                [
                    .. @event.Options.Select(o => new ChatPollChangedOptionDto(
                        o.Index,
                        o.Label,
                        o.Votes
                    )),
                ],
                @event.OpenedAt,
                @event.ClosesAt,
                @event.ClosedAt
            ),
            ct
        );
    }
}
