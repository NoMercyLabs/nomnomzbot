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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the savings-jars controller wires HTTP to <see cref="ISavingsJarService"/> (economy.md §5) and binds
/// the jar id from the route, the contributor from the authenticated caller, and a withdrawal's actor from the
/// caller — never from the body.
/// </summary>
public sealed class SavingsJarsControllerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000d01");
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000000d02");
    private static readonly Guid Jar = Guid.Parse("0192a000-0000-7000-8000-000000000d03");
    private static readonly Guid Spoofed = Guid.Parse("0192a000-0000-7000-8000-00000000dead");

    private static (SavingsJarsController Controller, ISavingsJarService Jars) Build()
    {
        ISavingsJarService jars = Substitute.For<ISavingsJarService>();
        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(Caller.ToString());
        return (new SavingsJarsController(jars, user), jars);
    }

    private static JarMovementDto Movement() =>
        new(1, Jar, Channel, Caller, 30, "Contribute", 30, 1, null, default);

    [Fact]
    public async Task Contribute_binds_the_jar_to_the_route_and_the_contributor_to_the_caller()
    {
        (SavingsJarsController controller, ISavingsJarService jars) = Build();
        jars.ContributeAsync(Channel, Arg.Any<JarContributeRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Movement()));

        IActionResult result = await controller.Contribute(
            Channel.ToString(),
            Jar,
            new JarContributeRequest(Spoofed, Spoofed, 30), // spoofed jar + contributor
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await jars.Received(1)
            .ContributeAsync(
                Channel,
                Arg.Is<JarContributeRequest>(c =>
                    c.JarId == Jar && c.ContributorUserId == Caller && c.Amount == 30
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Withdraw_binds_the_jar_to_the_route_and_the_actor_to_the_caller()
    {
        (SavingsJarsController controller, ISavingsJarService jars) = Build();
        jars.WithdrawAsync(Channel, Arg.Any<JarWithdrawRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Movement()));

        IActionResult result = await controller.Withdraw(
            Channel.ToString(),
            Jar,
            new JarWithdrawRequest(Spoofed, Caller, 20, Spoofed), // spoofed jar + actor
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await jars.Received(1)
            .WithdrawAsync(
                Channel,
                Arg.Is<JarWithdrawRequest>(w => w.JarId == Jar && w.ActorUserId == Caller),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ListJars_rejects_a_malformed_channel_id()
    {
        (SavingsJarsController controller, _) = Build();

        IActionResult result = await controller.ListJars("not-a-guid", default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
