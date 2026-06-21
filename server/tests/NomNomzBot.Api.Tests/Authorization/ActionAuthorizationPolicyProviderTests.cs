// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NomNomzBot.Api.Authorization;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// Proves the dynamic policy provider (roles-permissions §6): an <c>rbac:&lt;actionKey&gt;</c> policy name is
/// synthesized into an authenticated policy carrying exactly one <see cref="ActionAuthorizationRequirement"/>
/// for that key; any other policy name falls through to the framework default.
/// </summary>
public sealed class ActionAuthorizationPolicyProviderTests
{
    private static ActionAuthorizationPolicyProvider Provider() =>
        new(Options.Create(new AuthorizationOptions()));

    [Fact]
    public async Task Synthesizes_an_action_requirement_for_an_rbac_policy_name()
    {
        AuthorizationPolicy? policy = await Provider()
            .GetPolicyAsync(ActionAuthorizationPolicy.For("economy:config:write"));

        policy.Should().NotBeNull();
        policy!
            .Requirements.OfType<ActionAuthorizationRequirement>()
            .Single()
            .ActionKey.Should()
            .Be("economy:config:write");
    }

    [Fact]
    public async Task Falls_back_for_a_non_rbac_policy_name()
    {
        AuthorizationPolicy? policy = await Provider().GetPolicyAsync("SomeUnregisteredPolicy");

        // No such named policy is registered → the default provider returns null (not an action policy).
        policy.Should().BeNull();
    }

    [Fact]
    public void Policy_round_trips_the_action_key()
    {
        string name = ActionAuthorizationPolicy.For("moderation:ban");

        ActionAuthorizationPolicy.TryGetActionKey(name, out string actionKey).Should().BeTrue();
        actionKey.Should().Be("moderation:ban");
        ActionAuthorizationPolicy.TryGetActionKey("plain", out _).Should().BeFalse();
    }
}
