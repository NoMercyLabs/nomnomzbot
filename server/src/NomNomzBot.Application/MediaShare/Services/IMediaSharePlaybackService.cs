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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.MediaShare.Dtos;

namespace NomNomzBot.Application.MediaShare.Services;

/// <summary>
/// The server side of the media-share player: it owns the queue, tells the overlay what to play, and
/// advances the queue when the overlay reports an item ended or failed.
/// </summary>
public interface IMediaSharePlaybackService
{
    /// <summary>Runs one playback command and returns the player state it leaves.</summary>
    Task<Result<MediaPlayerStateDto>> SendCommandAsync(
        Guid broadcasterId,
        MediaPlaybackCommandRequest request,
        CancellationToken ct = default
    );

    /// <summary>The last state the overlay reported for the channel, idle when none.</summary>
    MediaPlayerStateDto GetState(Guid broadcasterId);

    /// <summary>Takes the overlay's report: stores it, tells the dashboard, and advances on ended or error.</summary>
    Task<Result> ReportStateAsync(
        Guid broadcasterId,
        MediaPlayerStateReport report,
        CancellationToken ct = default
    );
}
