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

// Helix "Raids" category wire models (POST/DELETE /raids). These records deserialize straight from
// Twitch's snake_case JSON via the transport's naming policy — no per-property annotations. Twitch ids
// stay strings (the target channel is another broadcaster's id); the owning tenant is always passed in
// as a Guid method argument, never here.

/// <summary>The pending raid created by Start a Raid — when it was created and whether the target is mature.</summary>
public sealed record TwitchRaid(DateTimeOffset CreatedAt, bool IsMature);
