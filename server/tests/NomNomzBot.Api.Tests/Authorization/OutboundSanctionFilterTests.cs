// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// The producing half of the outbound-sanction guarantee. The transport refuses any write that nothing
/// sanctioned, which is only safe if the dashboard actually opens a scope — otherwise the gate is not a
/// safeguard, it is an outage: every moderation action a streamer takes would be refused.
///
/// <para>
/// So this asserts the scope is open DURING the action, carries the endpoint's own Gate-2 key, and is gone
/// afterwards. It deliberately drives the real filter rather than asserting on its inputs.
/// </para>
/// </summary>
public sealed class OutboundSanctionFilterTests
{
    [Fact]
    public async Task An_endpoint_that_passed_gate_two_runs_with_its_action_key_as_the_sanction()
    {
        OutboundSanctionAccessor sanctions = new();
        OutboundSanctionFilter filter = new(sanctions);
        Guid caller = Guid.CreateVersion7();
        OutboundSanction? observed = null;

        await filter.OnActionExecutionAsync(
            Context("moderation:moderator:write", caller),
            () =>
            {
                observed = sanctions.Current;
                return Task.FromResult<ActionExecutedContext>(null!);
            }
        );

        observed
            .Should()
            .NotBeNull("without this every dashboard write is refused by the transport");
        observed!.Basis.Should().Be(OutboundSanctionBasis.UserAction);
        // The action key is the defensible answer to "what authorised this" — losing it would leave an
        // outbound change attributable to nothing in particular.
        observed.Detail.Should().Be("moderation:moderator:write");
        observed.ActorUserId.Should().Be(caller);
    }

    [Fact]
    public async Task An_endpoint_with_no_action_key_opens_no_sanction()
    {
        // An ungated endpoint has proven nothing about the caller, so it must not be able to change another
        // platform's state. This is what ties "may I" and "can I" together.
        OutboundSanctionAccessor sanctions = new();
        OutboundSanctionFilter filter = new(sanctions);
        OutboundSanction? observed = null;

        await filter.OnActionExecutionAsync(
            Context(actionKey: null, caller: null),
            () =>
            {
                observed = sanctions.Current;
                return Task.FromResult<ActionExecutedContext>(null!);
            }
        );

        observed.Should().BeNull();
    }

    // There is deliberately no "the scope does not leak past the request" test here. An AsyncLocal set
    // inside an async method never flows back to its caller, so a filter scope cannot outlive the request
    // even if its using were dropped — a test asserting it could not fail, and a green light that cannot go
    // red is worse than none. Scope restoration IS observable on the accessor itself, and is proven there
    // (Infrastructure.Tests OutboundSanctionGateTests.The_sanction_lapses_when_its_scope_closes).

    private static ActionExecutingContext Context(string? actionKey, Guid? caller)
    {
        List<object> metadata = [];
        if (actionKey is not null)
            metadata.Add(new RequireActionAttribute(actionKey));

        ClaimsPrincipal user = new(
            new ClaimsIdentity(
                caller is null ? [] : [new Claim(ClaimTypes.NameIdentifier, caller.ToString()!)],
                "test"
            )
        );

        return new(
            new(
                new DefaultHttpContext { User = user },
                new RouteData(),
                new ControllerActionDescriptor { EndpointMetadata = metadata }
            ),
            [],
            new Dictionary<string, object?>(),
            controller: null!
        );
    }
}
