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

// Helix "Goals" category wire models (GET /goals). These records deserialize straight from Twitch's
// snake_case JSON via the transport's naming policy — no per-property annotations. Twitch ids stay
// strings (they are other users' / channels' ids); the owning tenant is always passed in as a Guid
// method argument, never here.

/// <summary>One creator goal with its current progress (Get Creator Goals).</summary>
public sealed record TwitchCreatorGoal(
    string Id,
    string BroadcasterId,
    string BroadcasterName,
    string BroadcasterLogin,
    string Type,
    string Description,
    int CurrentAmount,
    int TargetAmount,
    DateTimeOffset CreatedAt
);
