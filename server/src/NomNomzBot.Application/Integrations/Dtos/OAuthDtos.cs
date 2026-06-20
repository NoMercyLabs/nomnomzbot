// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Integrations.Dtos;

/// <summary>The connect request body: the progressive scope-set to request, and where to return after connect (integrations-oauth §5).</summary>
public sealed record ConnectIntegrationRequest(string ScopeSetKey, string? ReturnUrl);

/// <summary>The authorize URL the client opens plus the signed single-use state (integrations-oauth §4).</summary>
public sealed record OAuthStartDto(string AuthorizeUrl, string State);

/// <summary>The query parameters a provider sends back to the callback (integrations-oauth §4).</summary>
public sealed record OAuthCallbackParams(
    string? Code,
    string? State,
    string? Error,
    string? ErrorDescription
);

/// <summary>The result of a successful connect (integrations-oauth §4).</summary>
public sealed record OAuthCallbackResultDto(
    string Provider,
    string ProviderAccountName,
    IReadOnlyList<string> GrantedScopeSets,
    string RedirectTarget
);

/// <summary>Per-provider status for the integrations screen (integrations-oauth §4). No secrets.</summary>
public sealed record IntegrationStatusDto(
    string Provider,
    bool Connected,
    string? AccountName,
    IReadOnlyList<string> GrantedScopeSets,
    IReadOnlyDictionary<string, bool> Capabilities,
    bool NeedsReauth
);
