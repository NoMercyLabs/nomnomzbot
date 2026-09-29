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

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// The structured facts behind a refused song request, carried on <see cref="Result.ErrorData"/> next to the
/// typed error code. The chat layer phrases its own reply from these values, so it never parses
/// <see cref="Result.ErrorMessage"/> (which stays a log/API sentence). Only the fields that apply to the
/// refusal are set.
/// </summary>
/// <param name="TrackName">The track the request resolved to, when the refusal happened after the resolve.</param>
/// <param name="Artist">That track's artist, when known.</param>
/// <param name="RequestedBy">Who already requested the track (duplicate refusals); null when unknown.</param>
/// <param name="IsPlayingNow">True when the duplicate is the track that is playing right now, not a queued one.</param>
/// <param name="Limit">The configured limit that was hit (queue size or requests per viewer).</param>
/// <param name="TrustLevel">The minimum role a requester needs (for example "Follower").</param>
public sealed record MusicRequestRefusal(
    string? TrackName = null,
    string? Artist = null,
    string? RequestedBy = null,
    bool IsPlayingNow = false,
    int? Limit = null,
    string? TrustLevel = null
);
