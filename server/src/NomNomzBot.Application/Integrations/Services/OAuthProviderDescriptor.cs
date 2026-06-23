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
/// The single place a provider's OAuth specifics live (integrations-oauth §3.2). Adding a provider = adding
/// one descriptor; the connect flow (<c>IIntegrationOAuthService</c>) is generic over it. Scope sets
/// enumerate the FULL manageable surface (external-API-coverage rule); a feature requests only the subset
/// it needs. The client_id/secret are resolved per request from <c>ISystemCredentialsProvider</c> (vaulted
/// store → config), so the descriptor only carries the deployment BYOK flag.
/// </summary>
public sealed record OAuthProviderDescriptor(
    string Provider,
    string AuthorizeEndpoint,
    string TokenEndpoint,
    string? RevokeEndpoint,
    string AccountIdentityEndpoint,
    bool UsesPkce,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ScopeSets,
    bool IsByok
);
