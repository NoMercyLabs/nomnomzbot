// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
