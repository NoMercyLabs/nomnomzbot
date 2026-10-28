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
/// <param name="RetryMinutes">Minutes until a rate-limited provider takes calls again.</param>
/// <param name="RetryAt">When a provider whose daily quota is used up takes calls again.</param>
public sealed record MusicRequestRefusal(
    string? TrackName = null,
    string? Artist = null,
    string? RequestedBy = null,
    bool IsPlayingNow = false,
    int? Limit = null,
    int? RetryMinutes = null,
    DateTimeOffset? RetryAt = null
);
