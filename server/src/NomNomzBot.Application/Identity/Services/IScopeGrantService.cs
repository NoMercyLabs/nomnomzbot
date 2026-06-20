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

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// Progressive, grant-aware scopes (identity-auth §3.4a). Enabling a feature triggers NO OAuth when the
/// scopes it needs are already on the connection; a dropped scope degrades only the features that needed
/// it, never a blind re-auth. <c>IntegrationConnection.Scopes</c> is the stored grant set this service keeps
/// truthful and gates feature enablement on.
/// </summary>
public interface IScopeGrantService
{
    /// <summary>The scopes a feature requires (static FeatureScopeMap registry). Pure lookup.</summary>
    IReadOnlyList<string> RequiredScopesFor(string featureKey);

    /// <summary>
    /// Decides whether enabling <paramref name="featureKey"/> needs user interaction:
    /// required ⊆ granted → <c>AlreadyGranted=true</c>, no URL (enable now, zero OAuth); otherwise
    /// <c>AlreadyGranted=false</c> + an authorize URL requesting <c>granted ∪ required</c> (consent to just
    /// the delta).
    /// </summary>
    Task<Result<ScopeGrantState>> EnsureFeatureScopesAsync(
        Guid broadcasterId,
        string featureKey,
        string? baseUrl = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reconciles the stored <c>Scopes</c> to the AUTHORITATIVE granted set from a token response. Called on
    /// every token store/refresh. <c>dropped = previous \ actual</c>; if non-empty → emits
    /// <c>ScopesDroppedEvent</c> and disables every feature whose required scopes are no longer satisfied.
    /// Returns the dropped scopes.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> ReconcileGrantedScopesAsync(
        Guid connectionId,
        IReadOnlyList<string> actualScopes,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// The outcome of a grant-aware feature-enable check (identity-auth §3.4a): whether the scopes were already
/// granted, the incremental authorize URL to request the delta (null when already granted), and the missing
/// scopes.
/// </summary>
public sealed record ScopeGrantState(
    bool AlreadyGranted,
    string? IncrementalAuthorizeUrl,
    IReadOnlyList<string> MissingScopes
);
