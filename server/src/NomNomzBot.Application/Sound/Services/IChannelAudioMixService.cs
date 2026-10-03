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

namespace NomNomzBot.Application.Sound.Services;

/// <summary>The channel's overlay audio balance: a master volume and a text-to-speech volume, each 0..100.</summary>
public interface IChannelAudioMixService
{
    /// <summary>The channel's mix; a channel with no stored row gets 100/100 and nothing is written.</summary>
    Task<Result<ChannelAudioMixDto>> GetAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>Stores the mix (each volume clamped to 0..100), creating the row on first write.</summary>
    Task<Result<ChannelAudioMixDto>> UpdateAsync(
        Guid broadcasterId,
        UpdateChannelAudioMixRequest request,
        CancellationToken ct = default
    );
}

public sealed record UpdateChannelAudioMixRequest(int MasterVolume, int TtsVolume);

public sealed record ChannelAudioMixDto(int MasterVolume, int TtsVolume)
{
    /// <summary>A clip's own 0..100 volume scaled by the master volume.</summary>
    public int ApplyToClipVolume(int clipVolume) =>
        (int)Math.Round(clipVolume * MasterVolume / 100.0, MidpointRounding.AwayFromZero);

    /// <summary>The 0..1 gain for text-to-speech playback: tts volume times master volume.</summary>
    public double TtsPlaybackVolume => TtsVolume * MasterVolume / 10000.0;
}
