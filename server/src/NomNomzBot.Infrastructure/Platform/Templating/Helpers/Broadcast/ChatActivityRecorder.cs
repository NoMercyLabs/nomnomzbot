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
