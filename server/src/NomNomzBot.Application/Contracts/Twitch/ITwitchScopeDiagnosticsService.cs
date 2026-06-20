// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Twitch;

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// Builds the per-tenant Twitch scope/connection health matrix (twitch-helix.md §5). Read-only: it reads the
/// channel's Twitch <c>IntegrationConnection</c> (the login-truthful granted set) and diffs it against the
/// progressive feature→scope registry, so the dashboard can show exactly which features are unlocked and
/// which need a scope grant. Returns <c>NOT_FOUND</c> when the tenant has no Twitch connection.
/// </summary>
public interface ITwitchScopeDiagnosticsService
{
    Task<Result<TwitchScopeDiagnosticsDto>> GetScopeDiagnosticsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );
}
