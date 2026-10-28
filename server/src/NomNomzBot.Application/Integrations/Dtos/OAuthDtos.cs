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

namespace NomNomzBot.Application.Integrations.Dtos;

/// <summary>
/// The connect request body: the progressive scope-set to request, where to return after connect
/// (integrations-oauth §5), and — for a shop-scoped provider (Shopify) — the shop name (bare or pasted as
/// <c>name.myshopify.com</c>; required there, ignored elsewhere).
/// </summary>
public sealed record ConnectIntegrationRequest(
    string ScopeSetKey,
    string? ReturnUrl,
    string? ShopDomain = null
);

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

/// <summary>Per-provider status for the integrations screen (integrations-oauth §4). No secrets.
/// <see cref="LoginOnly"/> distinguishes a provider the channel owner merely SIGNED IN with (an identity-plane
/// login, e.g. Kick) from a genuine tenant-scoped platform connection — a login alone grants none of the
/// scopes the bot needs and must never be shown as "Connected". <see cref="NeedsReauth"/> is true for every
/// status only a fresh grant clears; <see cref="DecryptFailed"/> narrows it to a stored token that can no
/// longer be decrypted.</summary>
public sealed record IntegrationStatusDto(
    string Provider,
    bool Connected,
    string? AccountName,
    IReadOnlyList<string> GrantedScopeSets,
    IReadOnlyDictionary<string, bool> Capabilities,
    bool NeedsReauth,
    bool LoginOnly = false,
    bool DecryptFailed = false,
    Guid? ConnectionId = null
);
