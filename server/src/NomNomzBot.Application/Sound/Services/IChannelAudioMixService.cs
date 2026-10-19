// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Sound.Entities;

namespace NomNomzBot.Application.Sound.Services;

/// <summary>The channel's overlay audio balance: a master volume and a text-to-speech volume, each 0..100.</summary>
public interface IChannelAudioMixService
{
    /// <summary>The channel's mix; a channel with no stored row gets the defaults and nothing is written.</summary>
    Task<Result<ChannelAudioMixDto>> GetAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>
    /// Stores the mix, creating the row on first write. A volume outside the limits is refused with a
    /// localized key (<c>audio_mix_master_volume_out_of_range</c> / <c>audio_mix_tts_volume_out_of_range</c>).
    /// </summary>
    Task<Result<ChannelAudioMixDto>> UpdateAsync(
        Guid broadcasterId,
        UpdateChannelAudioMixRequest request,
        CancellationToken ct = default
    );
}

public sealed record UpdateChannelAudioMixRequest(int MasterVolume, int TtsVolume);

public sealed record ChannelAudioMixDto(int MasterVolume, int TtsVolume)
{
    /// <summary>The master volume a channel with no saved mix gets; the dashboard shows it, never its own copy.</summary>
    public int DefaultMasterVolume => AudioMixDefaults.MasterVolume;

    /// <summary>The TTS volume a channel with no saved mix gets.</summary>
    public int DefaultTtsVolume => AudioMixDefaults.TtsVolume;

    /// <summary>Lowest accepted volume.</summary>
    public int MinVolume => AudioMixDefaults.MinVolume;

    /// <summary>Highest accepted volume.</summary>
    public int MaxVolume => AudioMixDefaults.MaxVolume;

    /// <summary>A clip's own 0..100 volume scaled by the master volume.</summary>
    public int ApplyToClipVolume(int clipVolume) =>
        (int)Math.Round(clipVolume * MasterVolume / 100.0, MidpointRounding.AwayFromZero);

    /// <summary>The 0..1 gain for text-to-speech playback: tts volume times master volume.</summary>
    public double TtsPlaybackVolume => TtsVolume * MasterVolume / 10000.0;
}
