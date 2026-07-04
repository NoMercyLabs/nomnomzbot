// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Authorization;

/// <summary>
/// Synthesizes authorization policies on demand for the two dynamic planes, discriminated by namespace:
/// a <c>rbac:&lt;actionKey&gt;</c> name (the Gate-2 prefix) becomes an
/// <see cref="ActionAuthorizationRequirement"/>; a name that IS a seeded Plane-C permission key verbatim —
/// membership in the closed compile-time <see cref="IamPermissionKeys.All"/> catalog (platform-conventions §5:
/// "the policy name IS the <c>IamPermissions</c> key verbatim") — becomes a
/// <see cref="PlatformIamRequirement"/>. Every other policy name falls through to the framework default
/// provider.
/// </summary>
public sealed class ActionAuthorizationPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public ActionAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options) =>
        _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
        _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (ActionAuthorizationPolicy.TryGetActionKey(policyName, out string actionKey))
        {
            AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new ActionAuthorizationRequirement(actionKey))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        if (IamPermissionKeys.All.Contains(policyName))
        {
            AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PlatformIamRequirement(policyName))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}
