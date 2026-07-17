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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.PickLists.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.PickLists.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.PickLists;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves <c>GET /picklists/{id}/pick</c> resolves the list by id, samples a real entry through the real
/// <see cref="PickListService"/>, and returns it as the <see cref="PickListPreviewDto"/> the dashboard "Test"
/// button renders — and that an empty list surfaces the <c>PICKLIST_EMPTY</c> outcome as a 404, not a 500.
/// </summary>
public sealed class PickListsControllerPreviewTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static PickListsController Build(PickListsControllerTestDbContext db)
    {
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(Broadcaster);

        PickListService service = new(db, Substitute.For<IEventBus>());
        return new PickListsController(service, tenant);
    }

    [Fact]
    public async Task PreviewPick_returns_an_entry_that_belongs_to_the_seeded_list()
    {
        PickListsControllerTestDbContext db = PickListsControllerTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Broadcaster,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "998877",
                Name = "stoney_eagle",
                NameNormalized = "stoney_eagle",
            }
        );
        Guid listId = Guid.CreateVersion7();
        List<string> items = ["left hook", "right jab", "spinning kick"];
        db.PickLists.Add(
            new PickList
            {
                Id = listId,
                BroadcasterId = Broadcaster,
                Name = "fight_moves",
                Items = items,
            }
        );
        await db.SaveChangesAsync();

        PickListsController controller = Build(db);

        IActionResult result = await controller.PreviewPick(listId, CancellationToken.None);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<PickListPreviewDto> body =
            (StatusResponseDto<PickListPreviewDto>)ok.Value!;
        // The sampled value is a genuine member of the seeded list, not a stub or an empty string.
        items.Should().Contain(body.Data!.Pick);
    }

    [Fact]
    public async Task PreviewPick_returns_404_for_an_empty_list_rather_than_a_server_error()
    {
        PickListsControllerTestDbContext db = PickListsControllerTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Broadcaster,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "998877",
                Name = "stoney_eagle",
                NameNormalized = "stoney_eagle",
            }
        );
        Guid listId = Guid.CreateVersion7();
        db.PickLists.Add(
            new PickList
            {
                Id = listId,
                BroadcasterId = Broadcaster,
                Name = "empty_list",
                Items = [],
            }
        );
        await db.SaveChangesAsync();

        PickListsController controller = Build(db);

        IActionResult result = await controller.PreviewPick(listId, CancellationToken.None);

        // PICKLIST_EMPTY maps to 404 (the QUOTES_EMPTY precedent) — never the default 500 for an unmapped code.
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task PreviewPick_returns_404_when_the_list_id_does_not_exist()
    {
        PickListsControllerTestDbContext db = PickListsControllerTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Broadcaster,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "998877",
                Name = "stoney_eagle",
                NameNormalized = "stoney_eagle",
            }
        );
        await db.SaveChangesAsync();

        PickListsController controller = Build(db);

        IActionResult result = await controller.PreviewPick(
            Guid.CreateVersion7(),
            CancellationToken.None
        );

        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
