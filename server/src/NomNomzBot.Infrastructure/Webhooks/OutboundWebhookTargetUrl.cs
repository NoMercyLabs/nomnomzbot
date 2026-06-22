// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace NomNomzBot.Infrastructure.Webhooks;

/// <summary>
/// Builds an outbound webhook delivery URL from an allowlisted host plus a stored path, refusing any path that
/// would move the request to a different host. The delivery URL is <c>https://{fqdn}{path}</c>, so a path such
/// as <c>@evil.com/</c> would otherwise parse to userinfo <c>fqdn</c> and host <c>evil.com</c> — exfiltrating the
/// signed payload past the egress allowlist (webhooks.md H.7). Used both to validate a path when an endpoint is
/// created and to build the URL on delivery, so the two can never disagree.
/// </summary>
internal static class OutboundWebhookTargetUrl
{
    /// <summary>
    /// Returns true and the absolute https URL only when the path leaves the host equal to <paramref name="fqdn"/>
    /// and introduces no userinfo component; otherwise false (the path is rejected).
    /// </summary>
    public static bool TryBuild(string fqdn, string? path, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (
            !Uri.TryCreate(
                $"https://{fqdn}{path ?? string.Empty}",
                UriKind.Absolute,
                out Uri? built
            )
            || built.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(built.UserInfo)
            || !string.Equals(built.Host, fqdn, StringComparison.OrdinalIgnoreCase)
        )
            return false;

        uri = built;
        return true;
    }
}
