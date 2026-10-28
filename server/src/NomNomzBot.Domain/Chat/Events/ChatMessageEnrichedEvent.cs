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

    // ─── Who this card belongs to ────────────────────────────────────────────
    // The card renders inside the REQUESTER's own bubble, so it has to name them. An overlay that hides
    // command lines never rendered the "!sr ..." line and so has no earlier line to read a name off — with
    // the id alone such an overlay can only show the card unattributed, or not at all.

    /// <summary>Display name of the viewer this card belongs to.</summary>
    public string? UserDisplayName { get; init; }

    /// <summary>Lowercase login of the viewer this card belongs to.</summary>
    public string? UserLogin { get; init; }
}
