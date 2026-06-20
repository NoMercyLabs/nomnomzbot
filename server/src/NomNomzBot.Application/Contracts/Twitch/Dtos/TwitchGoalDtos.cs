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
