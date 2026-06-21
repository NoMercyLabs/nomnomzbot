// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Authorization;

/// <summary>
/// Maps a roles-permissions action key to an ASP.NET policy name and back. Action keys contain colons
/// (<c>economy:config:write</c>), so they are wrapped behind a distinct <see cref="Prefix"/> to keep them
/// from colliding with any built-in named policy.
/// </summary>
public static class ActionAuthorizationPolicy
{
    public const string Prefix = "rbac:";

    /// <summary>The policy name for an action key — e.g. <c>economy:config:write</c> → <c>rbac:economy:config:write</c>.</summary>
    public static string For(string actionKey) => Prefix + actionKey;

    /// <summary>Extracts the action key from a policy name produced by <see cref="For"/>.</summary>
    public static bool TryGetActionKey(string policyName, out string actionKey)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            actionKey = policyName[Prefix.Length..];
            return actionKey.Length > 0;
        }
        actionKey = string.Empty;
        return false;
    }
}
