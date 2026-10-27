// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>Feeds every chat message, on every platform, into the ring the <c>activity</c> helpers read.</summary>
internal sealed class ChatActivityRecorder(IEnumerable<ITemplateHelperProvider> providers)
    : IEventHandler<ChatMessageReceivedEvent>
{
    public Task HandleAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        providers
            .OfType<ChatActivityTemplateHelpers>()
            .FirstOrDefault()
            ?.Ring.Record(@event.BroadcasterId, @event.UserId, @event.UserDisplayName);
        return Task.CompletedTask;
    }
}
