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
