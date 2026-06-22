// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
