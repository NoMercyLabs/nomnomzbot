// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;

namespace NomNomzBot.Api.Tests.Requirements.Providers;

/// <summary>
/// Reflection helpers shared by the external-provider coverage requirement tests. They enumerate the method
/// surface that ACTUALLY EXISTS on the bot's typed provider clients/services, so a coverage test can compare
/// what exists to the capability set the provider's public API allows — a HARD project rule
/// (external-api-full-management-coverage): whatever an external service lets a caller do, the bot must expose.
/// The tests below are requirement tests: a red means a real coverage gap to ADD, not a broken test.
/// </summary>
internal static class ProviderSurface
{
    /// <summary>Every public method name declared across the given interface/type set (case-insensitive).</summary>
    internal static HashSet<string> MethodNames(params Type[] types)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (Type type in types)
        {
            foreach (
                MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            )
            {
                // Drop property accessors — we compare against endpoint operations, not getters.
                if (!method.IsSpecialName)
                {
                    names.Add(method.Name);
                }
            }
        }

        return names;
    }

    /// <summary>True when some method name contains ANY of the given keyword fragments (case-insensitive).</summary>
    internal static bool Covers(this HashSet<string> methodNames, params string[] keywords) =>
        methodNames.Any(name =>
            keywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        );
}
