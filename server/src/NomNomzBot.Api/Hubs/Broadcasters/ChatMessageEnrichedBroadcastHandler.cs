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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// A chat line the bot has since learned more about → a <c>ChatMessageEnriched</c> widget event, so the chat
/// overlay can replace that line's body with a card.
///
/// <para>
/// The song-request case is why this exists: <c>!sr never gonna give you up</c> has no url, so the
/// link-preview step cannot help it and the bubble is left showing raw command text. The provider's own
/// answer — name, artist, artwork — arrives here instead, which is better data than scraping OpenGraph
/// would have produced.
/// </para>
///
/// <para>
/// Routed through the same subscription-matched dispatch as every other widget event, so only widgets that
/// declare <c>ChatMessageEnriched</c> receive it. It carries no <c>channelEventId</c>: this is an update to
/// a line already on screen, not a new item for the activity feed, and filing it as one would put a second
/// entry in the feed for a single song request.
/// </para>
/// </summary>
public sealed class ChatMessageEnrichedBroadcastHandler(
    IApplicationDbContext db,
    IWidgetNotifier widgets
) : IEventHandler<ChatMessageEnrichedEvent>
{
    public async Task HandleAsync(
        ChatMessageEnrichedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        await WidgetAlertDispatch.RouteAsync(
            db,
            widgets,
            @event.BroadcasterId,
            "ChatMessageEnriched",
            new ChatMessageEnrichedWidgetPayload(
                @event.MessageId,
                @event.LinkUrl,
                @event.Title,
                @event.Description,
                @event.ImageUrl,
                @event.Provider,
                @event.UserDisplayName,
                @event.UserLogin
            ),
            excludeWidgetId: null,
            channelEventId: null,
            cancellationToken
        );
    }
}
