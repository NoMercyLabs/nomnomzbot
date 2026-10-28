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
