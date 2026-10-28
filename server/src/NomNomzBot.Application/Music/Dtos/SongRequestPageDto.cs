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

namespace NomNomzBot.Application.Music.Dtos;

/// <summary>
/// The channel context a public SR-page token resolves to (music-sr.md §3.7) — everything the anonymous
/// <c>/sr/{token}</c> page needs to render and gate the submit form, with no JWT and no PII.
/// </summary>
public sealed record SongRequestPageDto(
    Guid BroadcasterId,
    string ChannelName,
    bool IsAcceptingRequests,
    IReadOnlyList<string> EnabledProviders
);
