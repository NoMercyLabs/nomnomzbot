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
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Infrastructure.Moderation;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the <c>ChatFiltersController</c> management surface persists and reads back a real filter through the
/// real <see cref="ChatFilterService"/>: a created blocklist filter appears in the channel's paginated list with
/// its authored shape intact (name, type, terms, action, timeout, exemption floor), and a fetch-by-id returns the
/// same row — the create→get and create→list round-trips the dashboard editor depends on.
/// </summary>
public sealed class ChatFiltersControllerTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static ChatFiltersController Build(ChatFiltersControllerTestDbContext db) =>
        new(new ChatFilterService(db));

    private static CreateChatFilterRequest SpamFilter() =>
        new()
        {
            FilterType = ChatFilterType.Blocklist,
            Name = "no-spam",
            Action = ChatFilterAction.Timeout,
            Terms = ["buy followers", "free bits"],
            TimeoutSeconds = 300,
            ExemptMinRoleLevel = 4,
        };

    [Fact]
    public async Task Create_then_list_round_trips_the_filter()
    {
        ChatFiltersControllerTestDbContext db = ChatFiltersControllerTestDbContext.New();
        ChatFiltersController controller = Build(db);

        IActionResult created = await controller.CreateFilter(
            Broadcaster.ToString(),
            SpamFilter(),
            CancellationToken.None
        );

        CreatedAtActionResult createdAt = created
            .Should()
            .BeOfType<CreatedAtActionResult>()
            .Subject;
        ChatFilterDto createdDto = ((StatusResponseDto<ChatFilterDto>)createdAt.Value!).Data!;
        createdDto.Id.Should().NotBeEmpty();

        IActionResult listed = await controller.ListFilters(
            Broadcaster.ToString(),
            new PageRequestDto(),
            CancellationToken.None
        );

        OkObjectResult ok = listed.Should().BeOfType<OkObjectResult>().Subject;
        PaginatedResponse<ChatFilterDto> page = (PaginatedResponse<ChatFilterDto>)ok.Value!;

        // The list carries back exactly the one filter we created, with its authored shape intact.
        ChatFilterDto listedDto = page.Data.Should().ContainSingle().Subject;
        listedDto.Id.Should().Be(createdDto.Id);
        listedDto.Name.Should().Be("no-spam");
        listedDto.FilterType.Should().Be(ChatFilterType.Blocklist);
        listedDto.Action.Should().Be(ChatFilterAction.Timeout);
        listedDto.TimeoutSeconds.Should().Be(300);
        listedDto.ExemptMinRoleLevel.Should().Be(4);
        listedDto.Terms.Should().Equal("buy followers", "free bits");
        listedDto.IsEnabled.Should().BeTrue();
        listedDto.MatchCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_then_get_by_id_returns_the_same_filter()
    {
        ChatFiltersControllerTestDbContext db = ChatFiltersControllerTestDbContext.New();
        ChatFiltersController controller = Build(db);

        CreatedAtActionResult created = (CreatedAtActionResult)
            await controller.CreateFilter(
                Broadcaster.ToString(),
                SpamFilter(),
                CancellationToken.None
            );
        ChatFilterDto createdDto = ((StatusResponseDto<ChatFilterDto>)created.Value!).Data!;

        IActionResult fetched = await controller.GetFilter(
            Broadcaster.ToString(),
            createdDto.Id,
            CancellationToken.None
        );

        OkObjectResult ok = fetched.Should().BeOfType<OkObjectResult>().Subject;
        ChatFilterDto fetchedDto = ((StatusResponseDto<ChatFilterDto>)ok.Value!).Data!;
        fetchedDto.Id.Should().Be(createdDto.Id);
        fetchedDto.Name.Should().Be("no-spam");
        fetchedDto.Terms.Should().Equal("buy followers", "free bits");
    }

    [Fact]
    public async Task Get_an_absent_filter_is_a_404_not_a_500()
    {
        ChatFiltersControllerTestDbContext db = ChatFiltersControllerTestDbContext.New();
        ChatFiltersController controller = Build(db);

        IActionResult fetched = await controller.GetFilter(
            Broadcaster.ToString(),
            Guid.CreateVersion7(),
            CancellationToken.None
        );

        // NOT_FOUND maps to 404 through BaseController.ResultResponse — never the default 500.
        fetched.Should().BeOfType<NotFoundObjectResult>();
    }
}
