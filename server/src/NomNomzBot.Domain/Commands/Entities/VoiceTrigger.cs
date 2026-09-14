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
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// A spoken-word counter: fires when the streamer's voice-listener page (a real Chrome tab running
/// <c>webkitSpeechRecognition</c> — OBS's CEF browser source cannot run speech recognition at all) hears
/// <see cref="Word"/> in the transcribed audio. On a match the bot increments <see cref="CurrentCount"/> and
/// shows <see cref="StickerAssetId"/> on the overlay (an Alert-style widget, transient), mirroring how
/// StreamElements-style word counters behave. <see cref="StartingCount"/> lets a streamer migrating off another
/// bot preserve their existing tally instead of resetting to zero — it is set once at creation and never moves
/// again; <see cref="CurrentCount"/> is the live number that increments on every fire.
/// </summary>
public class VoiceTrigger : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>The phrase to match — case-insensitive substring match against the transcribed text.</summary>
    [MaxLength(200)]
    public string Word { get; set; } = null!;

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// The tally value StartingCount seeds <see cref="CurrentCount"/> with at creation — e.g. the number
    /// StreamElements already tracked for this word before the streamer migrated. Never changes after create.
    /// </summary>
    public int StartingCount { get; set; }

    /// <summary>The live, incrementing count. Starts equal to <see cref="StartingCount"/>.</summary>
    public int CurrentCount { get; set; }

    /// <summary>Per-trigger cooldown (seconds) guarding against stuttering/repeated speech re-firing instantly.</summary>
    public int CooldownSeconds { get; set; } = 5;

    /// <summary>The channel asset (image) shown on the overlay when this trigger fires.</summary>
    public Guid? StickerAssetId { get; set; }

    /// <summary>When this trigger last fired — the cooldown window's anchor. Null until the first fire.</summary>
    public DateTime? LastFiredAt { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
