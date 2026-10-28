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

namespace NomNomzBot.Api.Authorization;

/// <summary>
/// The open-redirect boundary for OAuth post-auth callbacks (§5): decides whether a client-supplied
/// <c>redirect_uri</c> is a permitted target for the post-auth response (which carries tokens). Only three shapes
/// are allowed:
/// <list type="bullet">
///   <item><b>blank</b> — the normal web flow (a JSON/fragment response, no redirect);</item>
///   <item><c>nomnomzbot://…</c> — the app's own custom scheme (mobile deep-link);</item>
///   <item>an <b>RFC-8252 §7.3 loopback</b> redirect — <c>http</c> on a loopback host
///     (<c>127.0.0.1</c> / <c>[::1]</c> / <c>localhost</c>), any ephemeral port (the desktop client's listener).</item>
/// </list>
/// Any other host is rejected, so a phishing link cannot redirect the tokens to an attacker. The loopback host is
/// validated by value via <see cref="Uri.IsLoopback"/>, so spoofs like <c>127.0.0.1.evil.com</c> or
/// <c>localhost.evil.com</c> — which parse to non-loopback hosts — are rejected, and an <c>https</c> or
/// non-loopback URL never passes.
/// </summary>
public static class ClientRedirectPolicy
{
    public static bool IsAllowed(string? redirectUri)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
            return true;

        if (redirectUri.StartsWith("nomnomzbot://", StringComparison.OrdinalIgnoreCase))
            return true;

        return Uri.TryCreate(redirectUri, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttp
            && uri.IsLoopback;
    }
}
