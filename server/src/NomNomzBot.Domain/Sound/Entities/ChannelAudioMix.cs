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

namespace NomNomzBot.Domain.Sound.Entities;

/// <summary>
/// A channel's overlay audio balance, stored on the bot so every streaming PC plays the same mix.
/// One row per channel (<c>Unique(BroadcasterId)</c>); a channel with no row plays at 100/100.
/// </summary>
public class ChannelAudioMix : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }

    /// <summary>Master volume, 0..100, applied to every sound clip and to text-to-speech.</summary>
    public int MasterVolume { get; set; } = 100;

    /// <summary>Text-to-speech volume, 0..100, applied on top of the master volume.</summary>
    public int TtsVolume { get; set; } = 100;
}
