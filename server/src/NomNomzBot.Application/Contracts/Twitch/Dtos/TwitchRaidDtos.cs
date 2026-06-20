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

// Helix "Raids" category wire models (POST/DELETE /raids). These records deserialize straight from
// Twitch's snake_case JSON via the transport's naming policy — no per-property annotations. Twitch ids
// stay strings (the target channel is another broadcaster's id); the owning tenant is always passed in
// as a Guid method argument, never here.

/// <summary>The pending raid created by Start a Raid — when it was created and whether the target is mature.</summary>
public sealed record TwitchRaid(DateTimeOffset CreatedAt, bool IsMature);
