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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the catalog controller wires HTTP to <see cref="ICatalogService"/> (economy.md §5) and — critically —
/// that a purchase's buyer + role level are taken from the authenticated caller / a server-side resolve (never
/// the body), the item id from the route, and a refund's actor from the caller.
/// </summary>
public sealed class CatalogControllerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000b01");
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000000b02");
    private static readonly Guid Item = Guid.Parse("0192a000-0000-7000-8000-000000000b03");
    private static readonly Guid Spoofed = Guid.Parse("0192a000-0000-7000-8000-00000000dead");

    private static (
        CatalogController Controller,
        ICatalogService Catalog,
        IRoleResolver Roles
    ) Build()
    {
        ICatalogService catalog = Substitute.For<ICatalogService>();
        IRoleResolver roles = Substitute.For<IRoleResolver>();
        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(Caller.ToString());
        return (new CatalogController(catalog, roles, user), catalog, roles);
    }

    private static CatalogPurchaseDto Purchase() =>
        new(1, Item, Caller, Guid.NewGuid(), 30, "Sound Alert", "Completed", 1, null, default);

    [Fact]
    public async Task Purchase_binds_buyer_to_caller_and_resolves_the_level_server_side()
    {
        (CatalogController controller, ICatalogService catalog, IRoleResolver roles) = Build();
        roles
            .ResolveEffectiveLevelAsync(Caller, Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(5));
        catalog
            .PurchaseAsync(Channel, Arg.Any<PurchaseRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Purchase()));
        // Body tries to spoof item, buyer, and an inflated role level.
        PurchaseRequest spoofed = new(Spoofed, Spoofed, "args", 999, "idem");

        IActionResult result = await controller.Purchase(
            Channel.ToString(),
            Item,
            spoofed,
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await catalog
            .Received(1)
            .PurchaseAsync(
                Channel,
                Arg.Is<PurchaseRequest>(p =>
                    p.ItemId == Item
                    && p.BuyerUserId == Caller
                    && p.RoleLevel == 5
                    && p.InputArgs == "args"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Refund_stamps_the_actor_from_the_caller()
    {
        (CatalogController controller, ICatalogService catalog, _) = Build();
        catalog
            .RefundPurchaseAsync(Channel, 7, Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Purchase()));

        IActionResult result = await controller.Refund(
            Channel.ToString(),
            7,
            new RefundRequest("oops", Spoofed),
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await catalog
            .Received(1)
            .RefundPurchaseAsync(
                Channel,
                7,
                Arg.Is<RefundRequest>(r => r.ActorUserId == Caller),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetItem_rejects_a_malformed_channel_id()
    {
        (CatalogController controller, _, _) = Build();

        IActionResult result = await controller.GetItem("not-a-guid", Item, default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
