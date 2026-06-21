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
/// Proves the currency controller wires HTTP to the economy services (economy.md §5): it parses the channel id,
/// returns the mapped result, and — critically — binds an admin adjust's subject to the ROUTE and its actor to
/// the authenticated CALLER (never the request body), and likewise stamps a transfer's actor from the caller.
/// </summary>
public sealed class CurrencyControllerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000a01");
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000000a02");
    private static readonly Guid RouteViewer = Guid.Parse("0192a000-0000-7000-8000-000000000a03");
    private static readonly Guid Spoofed = Guid.Parse("0192a000-0000-7000-8000-00000000dead");

    private static (
        CurrencyController Controller,
        ICurrencyConfigService Config,
        ICurrencyAccountService Accounts
    ) Build()
    {
        ICurrencyConfigService config = Substitute.For<ICurrencyConfigService>();
        ICurrencyAccountService accounts = Substitute.For<ICurrencyAccountService>();
        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(Caller.ToString());
        return (new CurrencyController(config, accounts, user), config, accounts);
    }

    private static CurrencyLedgerEntryDto Entry() =>
        new(
            1,
            1,
            Guid.NewGuid(),
            RouteViewer,
            50,
            50,
            "admin_adjust",
            null,
            null,
            null,
            null,
            null,
            Caller,
            default
        );

    [Fact]
    public async Task GetConfig_returns_ok_with_the_config()
    {
        (CurrencyController controller, ICurrencyConfigService config, _) = Build();
        config
            .GetConfigAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<CurrencyConfigDto?>(
                    new CurrencyConfigDto(
                        Guid.NewGuid(),
                        Channel,
                        "points",
                        null,
                        null,
                        true,
                        100,
                        null,
                        0,
                        default,
                        default
                    )
                )
            );

        IActionResult result = await controller.GetConfig(Channel.ToString(), default);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Adjust_binds_the_subject_to_the_route_and_the_actor_to_the_caller()
    {
        (CurrencyController controller, _, ICurrencyAccountService accounts) = Build();
        accounts
            .AdminAdjustAsync(Channel, Arg.Any<AdminAdjustCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Entry()));
        // A body that tries to spoof both the subject and the actor.
        AdminAdjustCommand spoofedBody = new(Spoofed, 50, "bonus", Spoofed);

        IActionResult result = await controller.Adjust(
            Channel.ToString(),
            RouteViewer,
            spoofedBody,
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await accounts
            .Received(1)
            .AdminAdjustAsync(
                Channel,
                Arg.Is<AdminAdjustCommand>(c =>
                    c.ViewerUserId == RouteViewer && c.ActorUserId == Caller && c.Amount == 50
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Transfer_stamps_the_actor_from_the_caller()
    {
        (CurrencyController controller, _, ICurrencyAccountService accounts) = Build();
        accounts
            .TransferAsync(Channel, Arg.Any<TransferCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TransferResultDto(Entry(), Entry())));
        TransferCommand spoofedBody = new(RouteViewer, Spoofed, 25, "gift", Spoofed);

        IActionResult result = await controller.Transfer(Channel.ToString(), spoofedBody, default);

        result.Should().BeOfType<OkObjectResult>();
        await accounts
            .Received(1)
            .TransferAsync(
                Channel,
                Arg.Is<TransferCommand>(c => c.ActorUserId == Caller),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetConfig_rejects_a_malformed_channel_id()
    {
        (CurrencyController controller, _, _) = Build();

        IActionResult result = await controller.GetConfig("not-a-guid", default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
