// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Games" wire models (GET /games, /games/top). This record deserializes straight from Twitch's
// snake_case JSON via the transport's naming policy — no per-property annotations. Both endpoints are
// App-token, free-text reads with no owning tenant, so every id stays a string (a game id, never a tenant).

/// <summary>
/// One game or category. Returned by both Get Games (looked up by id / name / IGDB id) and Get Top Games
/// (the most-watched broadcasts). <c>BoxArtUrl</c> is a template URL with <c>{width}</c>/<c>{height}</c>
/// placeholders; <c>IgdbId</c> is the game's IGDB identifier (empty when Twitch has no mapping).
/// </summary>
public sealed record TwitchGame(string Id, string Name, string BoxArtUrl, string IgdbId);
