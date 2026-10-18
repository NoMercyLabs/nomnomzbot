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
