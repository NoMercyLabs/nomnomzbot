// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The static registry mapping a feature key to the Twitch scopes it requires (identity-auth §3.4a). The
/// single source of truth for progressive, grant-aware scope decisions: enabling a feature consults this to
/// know whether the connection already holds what it needs (no OAuth) or must request a delta, and a
/// dropped scope disables exactly the features whose entry is no longer satisfied.
/// </summary>
public static class FeatureScopeMap
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Map = new Dictionary<
        string,
        IReadOnlyList<string>
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["subscriptions"] = ["channel:read:subscriptions"],
        ["bits"] = ["bits:read"],
        ["redemptions"] = ["channel:read:redemptions", "channel:manage:redemptions"],
        ["raids"] = ["channel:manage:raids"],
        ["broadcast"] = ["channel:manage:broadcast"],
        ["polls"] = ["channel:read:polls", "channel:manage:polls"],
        ["predictions"] = ["channel:read:predictions", "channel:manage:predictions"],
        ["followers"] = ["moderator:read:followers"],
        ["moderation"] = ["moderator:manage:banned_users", "moderator:manage:chat_messages"],
        ["automod"] = ["moderator:manage:automod"],
        ["vips"] = ["channel:read:vips", "channel:manage:vips"],
        ["chat_read"] = ["user:read:chat"],
        ["chat_send"] = ["user:write:chat"],
    };

    /// <summary>The scopes <paramref name="featureKey"/> needs, or an empty list when the feature is unknown.</summary>
    public static IReadOnlyList<string> RequiredScopesFor(string featureKey) =>
        Map.TryGetValue(featureKey, out IReadOnlyList<string>? scopes) ? scopes : [];

    /// <summary>Every feature whose required scopes are a subset of <paramref name="grantedScopes"/>.</summary>
    public static IReadOnlyList<string> FeaturesSatisfiedBy(
        IReadOnlyCollection<string> grantedScopes
    )
    {
        HashSet<string> granted = new(grantedScopes, StringComparer.OrdinalIgnoreCase);
        return [.. Map.Where(kv => kv.Value.All(granted.Contains)).Select(kv => kv.Key)];
    }
}
