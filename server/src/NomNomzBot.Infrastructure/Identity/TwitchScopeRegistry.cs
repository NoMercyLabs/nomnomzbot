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
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Reflects, once, over every Helix sub-client implementation in
/// <c>NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients</c> and collects every
/// <see cref="RequiresTwitchScopeAttribute"/> it finds into <see cref="AllDeclaredScopes"/> — the
/// single authoritative set of Twitch scopes the Helix layer actually enforces at runtime.
/// <see cref="AuthService"/> unions this with a small residual set of non-Helix-gated scopes
/// (EventSub-only topics, <c>user:read:email</c>, bot-identity IRC-legacy scopes, …) to build the
/// login scope request, so a new <c>[RequiresTwitchScope]</c> method can never silently go
/// unrequested — the drift that let <c>moderator:manage:shoutouts</c> go missing for weeks.
/// Scanning the whole assembly (rather than a hand-maintained list of sub-client class names) means
/// a 27th sub-client added later is picked up automatically, with no second place to remember.
/// </summary>
public sealed class TwitchScopeRegistry
{
    private const string SubClientsNamespace =
        "NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients";

    /// <summary>
    /// Every scope string declared via <see cref="RequiresTwitchScopeAttribute"/> across the Helix
    /// sub-client assembly. Computed once at construction — this type is registered as a singleton.
    /// </summary>
    public IReadOnlySet<string> AllDeclaredScopes { get; }

    public TwitchScopeRegistry()
        : this(typeof(TwitchScopeRegistry).Assembly) { }

    /// <summary>Internal seam for tests that want to reflect over a different assembly.</summary>
    internal TwitchScopeRegistry(Assembly subClientAssembly)
    {
        AllDeclaredScopes = CollectDeclaredScopes(subClientAssembly);
    }

    private static IReadOnlySet<string> CollectDeclaredScopes(Assembly subClientAssembly)
    {
        HashSet<string> scopes = new(StringComparer.Ordinal);

        IEnumerable<Type> subClientTypes = subClientAssembly
            .GetTypes()
            .Where(type =>
                type.IsClass
                && string.Equals(type.Namespace, SubClientsNamespace, StringComparison.Ordinal)
            );

        foreach (Type subClientType in subClientTypes)
        {
            const BindingFlags memberFlags =
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly;

            foreach (MethodInfo method in subClientType.GetMethods(memberFlags))
            foreach (
                RequiresTwitchScopeAttribute attribute in method.GetCustomAttributes<RequiresTwitchScopeAttribute>(
                    inherit: false
                )
            )
                scopes.Add(attribute.Scope);
        }

        return scopes;
    }
}
