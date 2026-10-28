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
