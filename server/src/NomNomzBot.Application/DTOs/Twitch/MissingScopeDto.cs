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
/// The channel's outstanding Twitch scope gaps (identity-auth §3.4a) — the read model the dashboard banner
/// renders and the one-click re-grant acts on. <see cref="Scopes"/> is the deduplicated set of scopes the
/// streamer token is missing (proactive feature-gated gaps the connection never held + reactive gaps a real
/// Helix call surfaced); empty when the connection holds everything every offered feature needs.
/// </summary>
public sealed record MissingScopesDto(
    string ConnectionStatus,
    IReadOnlyList<MissingScopeDto> Scopes
);

/// <summary>
/// One missing-scope row: the absent scope, the feature(s) it blocks (human-grouped by the
/// <c>FeatureScopeMap</c> key), whether a real Helix call already failed for it (<see cref="DetectedAtRuntime"/>),
/// and whether the streamer has already been told in chat (<see cref="ChatNotified"/>). The dashboard renders
/// "the bot needs '&lt;scope&gt;' to &lt;features&gt;" from this and the [Grant permission] button re-grants the union.
/// </summary>
public sealed record MissingScopeDto(
    string Scope,
    IReadOnlyList<string> Features,
    bool DetectedAtRuntime,
    bool ChatNotified
);

/// <summary>
/// A started re-grant device authorization (identity-auth §3.4a): the same secret-free Device Code Flow the
/// streamer login uses, but requesting <c>granted ∪ missing</c> so the existing grant is never dropped. The
/// client shows <see cref="UserCode"/> at <see cref="VerificationUri"/> and polls the normal streamer device
/// poll with <see cref="DeviceCode"/>; on approval the token store reconciles the new scopes and the gap clears.
/// </summary>
public sealed record ScopeRegrantStartDto(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int Interval,
    int ExpiresIn,
    IReadOnlyList<string> RequestedScopes
);
