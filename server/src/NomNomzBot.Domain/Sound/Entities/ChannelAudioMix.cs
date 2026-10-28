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
    public int MasterVolume { get; set; } = AudioMixDefaults.MasterVolume;

    /// <summary>Text-to-speech volume, 0..100, applied on top of the master volume.</summary>
    public int TtsVolume { get; set; } = AudioMixDefaults.TtsVolume;

    /// <summary>Share of the music level that stays while bot audio speaks, 0..100; 100 leaves the music alone.</summary>
    public int MusicDuckPercent { get; set; } = AudioMixDefaults.MusicDuckPercent;
}
