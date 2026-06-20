// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.DTOs.Twitch;

/// <summary>
/// The Twitch scope/connection health read model (twitch-helix.md §5): the connection status, the granted
/// scope set, and the per-feature requirement matrix the dashboard renders so a missing scope is observable
/// (it closes the "subscriber count always 0" / "403 chat" class of silent failures by surfacing the gap).
/// </summary>
public sealed record TwitchScopeDiagnosticsDto(
    string ConnectionStatus,
    IReadOnlyList<string> GrantedScopes,
    IReadOnlyList<TwitchScopeRequirementDto> Requirements
);

/// <summary>
/// One row of the diagnostics matrix: a scope a feature needs, whether the connection currently holds it, and
/// (because every entry here is a progressive, feature-gated scope) the feature that gates its request. A
/// missing progressive scope is "feature-gated", not an error — it is requested when the feature is enabled.
/// </summary>
public sealed record TwitchScopeRequirementDto(
    string Scope,
    string Feature,
    bool Granted,
    bool IsProgressive,
    string? GatedByFeature
);
