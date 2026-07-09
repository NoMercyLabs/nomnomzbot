// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// The handshake shapes a login provider supports (platform-identity §3.2). A provider may support more than
/// one; the client picks per <see cref="LoginProviderDescriptor.SupportedFlows"/>.
/// </summary>
[Flags]
public enum LoginFlows
{
    None = 0,

    /// <summary>OAuth 2.0 Device Authorization Grant (secret-free; Twitch/Google). Wire token <c>device_code</c>.</summary>
    DeviceCode = 1,

    /// <summary>Authorization Code + PKCE (Kick). Wire token <c>auth_code_pkce</c>.</summary>
    AuthCodePkce = 2,

    /// <summary>Authorization Code (redirect) flow. Wire token <c>auth_code</c>.</summary>
    AuthCode = 4,
}

/// <summary>
/// A login provider as data, not a code fork (platform-identity §3.2) — mirrors the integration-OAuth
/// <c>OAuthProviderDescriptor</c> pattern. Registering a descriptor + flipping <see cref="FeatureFlagKey"/>
/// enables a provider with zero rewrites. <see cref="LoginScopes"/> are the minimal identify scopes for a
/// LOGIN (never the broader streamer scopes).
/// </summary>
public sealed record LoginProviderDescriptor(
    string Key,
    string DisplayName,
    LoginFlows SupportedFlows,
    string FeatureFlagKey,
    IReadOnlyList<string> LoginScopes
);
