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

namespace NomNomzBot.Domain.Tts.Entities;

public class TtsUsageRecord : BaseEntity, ITenantScoped
{
    public int Id { get; set; }
    public Guid BroadcasterId { get; set; }

    [MaxLength(50)]
    public string UserId { get; set; } = null!;

    public int CharacterCount { get; set; }

    [MaxLength(50)]
    public string Provider { get; set; } = null!;

    [MaxLength(255)]
    public string VoiceId { get; set; } = null!;

    /// <summary>Whether the profanity censor altered the spoken text (tts.md §3.5) — the ledger records what was actually said.</summary>
    public bool WasCensored { get; set; }

    /// <summary>True when a moderator approved the utterance from the P.1a queue; null when the channel dispatched directly.</summary>
    public bool? WasModApproved { get; set; }

    /// <summary>The live stream the utterance played during, when one was running.</summary>
    public Guid? StreamId { get; set; }

    /// <summary>When the utterance was dispatched to the overlay.</summary>
    public DateTime OccurredAt { get; set; }
}
