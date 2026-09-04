// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Chat.Events;

/// <summary>
/// A chat line already on screen has gained something worth showing — raised when the bot LEARNS what a
/// message actually meant, after the line was broadcast.
///
/// <para>
/// A song request is the case this exists for. <c>!sr never gonna give you up</c> carries no link, so the
/// link-preview step has nothing to work with, and the overlay is left showing raw command text. The bot
/// then resolves the query to a real track — title, artist, artwork — and this event carries that back so
/// the overlay can replace that line's body with the track's card.
/// </para>
///
/// <para>
/// Deliberately keyed on <see cref="MessageId"/> rather than re-broadcasting the message: the line is
/// already rendered, and re-sending it would duplicate it in every overlay that had shown it. A consumer
/// that does not know the id simply ignores the event.
/// </para>
/// </summary>
public sealed class ChatMessageEnrichedEvent : DomainEventBase
{
    /// <summary>The id of the already-broadcast chat line this enriches.</summary>
    public required string MessageId { get; init; }

    /// <summary>A clickable web url for the subject, when there is one.</summary>
    public string? LinkUrl { get; init; }

    /// <summary>Card title — for a song request, the track name.</summary>
    public string? Title { get; init; }

    /// <summary>Card subtitle — for a song request, the artist.</summary>
    public string? Description { get; init; }

    /// <summary>Card artwork — for a song request, the album art the provider already returned.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Where the source of this card came from (e.g. <c>spotify</c>, <c>youtube</c>).</summary>
    public string? Provider { get; init; }
}
