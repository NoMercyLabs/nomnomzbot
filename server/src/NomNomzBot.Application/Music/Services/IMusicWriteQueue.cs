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

namespace NomNomzBot.Application.Music.Services;

/// <summary>The kind of library write a favorite or a banger makes.</summary>
public enum MusicWriteKind
{
    /// <summary>Spotify: save to Liked Songs. YouTube: like the video.</summary>
    Save,

    /// <summary>Spotify: remove from Liked Songs. YouTube: remove the like.</summary>
    Unsave,

    /// <summary>Add tracks to a playlist.</summary>
    PlaylistAdd,
}

/// <summary>What the queue did with a write.</summary>
public enum MusicWriteOutcome
{
    /// <summary>The write reached the provider and succeeded.</summary>
    Sent,

    /// <summary>The write is held and goes out when the stream ends.</summary>
    Held,
}

/// <param name="BroadcasterId">The channel whose provider connection makes the write.</param>
/// <param name="Provider">The provider key, for example <c>spotify</c> or <c>youtube</c>.</param>
/// <param name="Kind">Which write to make.</param>
/// <param name="TrackUris">The tracks to write.</param>
/// <param name="PlaylistId">The target playlist, for <see cref="MusicWriteKind.PlaylistAdd"/> only.</param>
public sealed record MusicWriteRequest(
    Guid BroadcasterId,
    string Provider,
    MusicWriteKind Kind,
    IReadOnlyList<string> TrackUris,
    string? PlaylistId = null
);

/// <summary>
/// The one door every favorite and banger write goes through. Under the provider's budget a write goes
/// out at once; when the predicted use for the stream would pass the budget it is held and flushed when
/// the stream ends. A hold or a failure on one provider never touches another provider's writes.
/// </summary>
public interface IMusicWriteQueue
{
    Task<Result<MusicWriteOutcome>> SubmitAsync(
        MusicWriteRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sends every held write of the channel; returns how many went out.</summary>
    Task<int> FlushAsync(Guid broadcasterId, CancellationToken cancellationToken = default);

    /// <summary>Sends every held write of every channel, including those held before a restart; returns how many went out.</summary>
    Task<int> FlushAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Records provider units spent outside the queue, such as making a playlist.</summary>
    void Charge(string provider, int units);
}
