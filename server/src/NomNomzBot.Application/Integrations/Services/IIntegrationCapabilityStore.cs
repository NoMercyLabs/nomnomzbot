// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Integrations.Services;

/// <summary>
/// Runtime-observed integration capabilities (integrations-oauth §3 / music-sr.md §3.5), keyed
/// per tenant + provider — e.g. <c>spotify.premium</c> flips to false when a Spotify player write
/// comes back 403 <c>PREMIUM_REQUIRED</c>, and back to true on a successful player write.
/// Observations feed <c>IntegrationStatusDto.Capabilities</c> so the dashboard can disable
/// controls instead of letting them fail. In-memory by design: a capability is only *known*
/// once observed this process lifetime; absent means unknown, never false.
/// </summary>
public interface IIntegrationCapabilityStore
{
    /// <summary>Records an observation of one capability for (tenant, provider).</summary>
    void Report(Guid broadcasterId, string provider, string capability, bool supported);

    /// <summary>All capabilities observed so far for (tenant, provider); empty when none.</summary>
    IReadOnlyDictionary<string, bool> GetObserved(Guid broadcasterId, string provider);
}
