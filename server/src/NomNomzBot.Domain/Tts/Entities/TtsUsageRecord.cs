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

    /// <summary>The text that was spoken (after the lexicon and the censor). Null on rows written before the text was stored.</summary>
    [MaxLength(2000)]
    public string? Text { get; set; }

    /// <summary>Whether the profanity censor altered the spoken text (tts.md §3.5) — the ledger records what was actually said.</summary>
    public bool WasCensored { get; set; }

    /// <summary>True when a moderator approved the utterance from the P.1a queue; null when the channel dispatched directly.</summary>
    public bool? WasModApproved { get; set; }

    /// <summary>The live stream the utterance played during, when one was running.</summary>
    public Guid? StreamId { get; set; }

    /// <summary>When the utterance was dispatched to the overlay.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>How long the synthesis call took, in milliseconds; for a failed clip, the time spent until it failed. Null on rows written before the timing was kept.</summary>
    public int? SynthesisMs { get; set; }

    /// <summary>True when the provider threw, timed out or returned no audio; such a row was never spoken.</summary>
    public bool Failed { get; set; }
}
