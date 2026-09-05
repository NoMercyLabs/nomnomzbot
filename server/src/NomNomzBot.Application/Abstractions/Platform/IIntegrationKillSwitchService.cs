// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Platform;

/// <summary>
/// One integration's live availability for a tenant, as decided by <see cref="IIntegrationKillSwitchService"/>.
/// <see cref="Enabled"/> is false both when the integration was deliberately killed AND when the check itself
/// could not run — either way the caller degrades to a clean disabled state, never a thrown exception.
/// </summary>
/// <param name="Enabled">Whether the dependent feature may call out to the integration right now.</param>
/// <param name="Reason">Null when enabled; otherwise a short machine-readable reason for the disabled state.</param>
public sealed record IntegrationAvailability(bool Enabled, string? Reason);

/// <summary>
/// A per-integration kill switch built on top of <see cref="IFeatureFlagService"/> (flag key convention
/// <c>integration:{name}</c>, e.g. <c>integration:spotify</c>) — an operator kills an integration platform-wide
/// (global toggle) or for one tenant (per-tenant override) exactly like any other flag, via the existing
/// <c>FeatureFlagAdminController</c>. The point of this wrapper is the "degrades gracefully" contract: an
/// integration-owning service calls <see cref="CheckAsync"/> at the top of its call path and NEVER lets a flag
/// lookup failure escalate into a 500 for a viewer-facing feature — a killed OR unreachable flag both fold to a
/// clean disabled result.
/// </summary>
public interface IIntegrationKillSwitchService
{
    /// <summary>
    /// Whether <paramref name="integrationKey"/> (e.g. <c>spotify</c>, <c>discord</c>) is available for
    /// <paramref name="broadcasterId"/> right now. No flag defined for the integration means "available" — the
    /// switch is opt-OUT (kill), not opt-in. Never throws: any failure evaluating the underlying flag is caught
    /// and folded into a disabled result.
    /// </summary>
    Task<IntegrationAvailability> CheckAsync(
        string integrationKey,
        Guid broadcasterId,
        CancellationToken ct = default
    );
}
