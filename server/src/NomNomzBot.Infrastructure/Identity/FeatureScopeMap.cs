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
        ["chat_emotes"] = ["user:read:emotes"],
    };

    /// <summary>
    /// The full feature→scopes registry, for callers that need the whole matrix rather than one lookup (the
    /// scope-diagnostics endpoint flattens this into its per-feature requirement rows). Read-only.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Features => Map;

    /// <summary>
    /// Short, user-facing descriptions of what each feature does, for the missing-scope chat notice ("I need
    /// '&lt;scope&gt;' to &lt;describe&gt;"). Keyed by the same feature keys as <see cref="Features"/>; a feature
    /// with no entry falls back to its key.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["subscriptions"] = "read your subscriber list and count",
        ["bits"] = "see your Bits events",
        ["redemptions"] = "manage your channel point rewards",
        ["raids"] = "start and manage raids",
        ["broadcast"] = "update your stream title, game and tags",
        ["polls"] = "run and read your polls",
        ["predictions"] = "run and read your predictions",
        ["followers"] = "read your follower list and count",
        ["moderation"] = "moderate chat (timeouts, bans, message removal)",
        ["automod"] = "manage your AutoMod settings",
        ["vips"] = "read and manage your VIPs",
        ["chat_read"] = "read your chat",
        ["chat_send"] = "send messages in your chat",
        ["chat_emotes"] = "show the emotes you can use across every channel you're subscribed to",
    };

    /// <summary>The scopes <paramref name="featureKey"/> needs, or an empty list when the feature is unknown.</summary>
    public static IReadOnlyList<string> RequiredScopesFor(string featureKey) =>
        Map.TryGetValue(featureKey, out IReadOnlyList<string>? scopes) ? scopes : [];

    /// <summary>
    /// The first feature whose required-scope set contains <paramref name="scope"/>, or null when no offered
    /// feature needs it (a raw Helix scope detected outside the feature registry). Used to attribute a
    /// runtime-detected gap to a feature for the chat notice and the dashboard grouping.
    /// </summary>
    public static string? FeatureForScope(string scope) =>
        Map.FirstOrDefault(kv => kv.Value.Contains(scope, StringComparer.OrdinalIgnoreCase)).Key;

    /// <summary>A short user-facing description of <paramref name="featureKey"/>, falling back to the key itself.</summary>
    public static string DescribeFeature(string featureKey) =>
        Descriptions.TryGetValue(featureKey, out string? description) ? description : featureKey;

    /// <summary>Every feature whose required scopes are a subset of <paramref name="grantedScopes"/>.</summary>
    public static IReadOnlyList<string> FeaturesSatisfiedBy(
        IReadOnlyCollection<string> grantedScopes
    )
    {
        HashSet<string> granted = new(grantedScopes, StringComparer.OrdinalIgnoreCase);
        return [.. Map.Where(kv => kv.Value.All(granted.Contains)).Select(kv => kv.Key)];
    }
}
