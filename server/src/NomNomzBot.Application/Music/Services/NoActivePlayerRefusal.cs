// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// The <c>errorData</c> on a <c>NO_ACTIVE_DEVICE</c> playback-control failure: which provider found no player.
/// The fix differs by provider — open the overlay Audio widget for YouTube, start a Spotify device for Spotify —
/// so chat replies read it instead of guessing.
/// </summary>
public sealed record NoActivePlayerRefusal(string Provider);
