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
