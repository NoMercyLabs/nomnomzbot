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

namespace NomNomzBot.Application.Integrations.Services;

/// <summary>
/// The registry of OAuth provider descriptors (integrations-oauth §3.2). Holds every provider's authorize/
/// token endpoints + scope sets and resolves its credentials per deployment profile (BYOK self-host vs
/// platform SaaS). A new provider plugs in as one descriptor here — no new controller or service.
/// </summary>
public interface IOAuthProviderRegistry
{
    /// <summary>
    /// Resolves the descriptor for <paramref name="provider"/> with credentials selected for the given
    /// tenant's deployment profile. Failure (<c>UNKNOWN_PROVIDER</c>) when the provider is not registered.
    /// </summary>
    Result<OAuthProviderDescriptor> Resolve(string provider, Guid broadcasterId);

    /// <summary>The providers this registry can resolve (e.g. <c>spotify</c>, <c>youtube</c>).</summary>
    IReadOnlyList<string> KnownProviders { get; }
}
