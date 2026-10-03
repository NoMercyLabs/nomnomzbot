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

namespace NomNomzBot.Domain.Commands.Events;

/// <summary>
/// Published when a <see cref="Entities.VoiceTrigger"/> fires (the voice-listener page heard the word and
/// reported it, cooldown allowed it). Routed to the overlay's sticker-display widgets by
/// <c>VoiceTriggerWidgetEventHandler</c>, mirroring how <c>GoalWidgetEventHandler</c> routes creator-goal events.
/// </summary>
public sealed class VoiceTriggerFiredEvent : DomainEventBase
{
    /// <summary>The id of the voice trigger that fired.</summary>
    public required Guid VoiceTriggerId { get; init; }

    /// <summary>The word that the voice trigger heard.</summary>
    public required string Word { get; init; }

    /// <summary>How many times this trigger has fired, including this time.</summary>
    public required int NewCount { get; init; }

    /// <summary>The sticker image's public serving URL, resolved at fire time so the widget never has to look it up.</summary>
    public string? StickerImageUrl { get; init; }
}
