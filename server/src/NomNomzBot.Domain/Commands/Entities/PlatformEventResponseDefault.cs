// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// The platform default for one event type (plan item A4): what every channel whose <see cref="EventResponse"/>
/// row still follows the platform default does when that event fires. GLOBAL (no tenant). Seeded one row per
/// catalogue event type (no message — the tone catalogue speaks until an admin writes one); the seeder only ever
/// nulls a message that still equals the legacy seeded line, so the platform admin's edit survives every redeploy. A channel that saves its own response stops
/// following this row; resetting its response makes it follow again.
/// </summary>
public class PlatformEventResponseDefault : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    [MaxLength(100)]
    public string EventType { get; set; } = null!;

    public bool IsEnabled { get; set; }

    /// <summary>
    /// The platform admin's chat message template. Null means no admin text: following channels speak a line
    /// from the tone catalogue in their own personality tone. Text set here wins for every tone.
    /// </summary>
    [MaxLength(2000)]
    public string? Message { get; set; }

    /// <summary>Following channels also speak the resolved message through their TTS. Opt-in: false by default.</summary>
    public bool SpeakWithTts { get; set; }

    /// <summary>The platform operator who last changed this default.</summary>
    public Guid? UpdatedByUserId { get; set; }
}
