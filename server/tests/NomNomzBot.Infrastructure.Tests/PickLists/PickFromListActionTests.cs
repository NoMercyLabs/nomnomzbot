// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PickLists.Services;
using NomNomzBot.Infrastructure.PickLists.PipelineActions;
using NSubstitute;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.PickLists;

/// <summary>
/// Proves the <c>pick_from_list</c> pipeline action: it draws one entry from the named list and stores it in a
/// pipeline variable (default <c>pick</c>, overridable) for later steps, and fails loudly — without touching the
/// variables — on a missing <c>list</c> param or a service rejection (empty/unknown list).
/// </summary>
public sealed class PickFromListActionTests
{
    private static readonly Guid Channel = Guid.Parse("019f2a00-3333-7000-8000-000000000001");

    private static PipelineExecutionContext Context()
    {
        return new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "viewer-9",
            TriggeredByDisplayName = "viewer",
            MessageId = "m1",
            RawMessage = "!fight",
            CancellationToken = default,
        };
    }

    private static ActionDefinition Action(params (string Key, object Value)[] p) =>
        new()
        {
            Type = "pick_from_list",
            Parameters = p.ToDictionary(
                x => x.Key,
                x => JsonSerializer.SerializeToElement(x.Value)
            ),
        };

    [Fact]
    public async Task Picks_and_stores_into_the_default_variable()
    {
        IPickListService lists = Substitute.For<IPickListService>();
        lists
            .PickRandomAsync(Channel, "fight_moves", Arg.Any<CancellationToken>())
            .Returns(Result.Success("{user} bonks {target}"));
        PickFromListAction action = new(lists);
        PipelineExecutionContext ctx = Context();

        ActionResult result = await action.ExecuteAsync(ctx, Action(("list", "fight_moves")));

        result.Succeeded.Should().BeTrue();
        ctx.Variables["pick"].Should().Be("{user} bonks {target}");
    }

    [Fact]
    public async Task Stores_into_a_custom_variable_name()
    {
        IPickListService lists = Substitute.For<IPickListService>();
        lists
            .PickRandomAsync(Channel, "greetings", Arg.Any<CancellationToken>())
            .Returns(Result.Success("hi there"));
        PickFromListAction action = new(lists);
        PipelineExecutionContext ctx = Context();

        await action.ExecuteAsync(ctx, Action(("list", "greetings"), ("variable", "greeting")));

        ctx.Variables["greeting"].Should().Be("hi there");
    }

    [Fact]
    public async Task Missing_list_fails_without_picking()
    {
        IPickListService lists = Substitute.For<IPickListService>();
        PickFromListAction action = new(lists);

        ActionResult result = await action.ExecuteAsync(Context(), Action());

        result.Succeeded.Should().BeFalse();
        await lists
            .DidNotReceive()
            .PickRandomAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_list_surfaces_the_service_reason()
    {
        IPickListService lists = Substitute.For<IPickListService>();
        lists
            .PickRandomAsync(Channel, "empty", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<string>("That pick list has no entries.", "PICKLIST_EMPTY"));
        PickFromListAction action = new(lists);
        PipelineExecutionContext ctx = Context();

        ActionResult result = await action.ExecuteAsync(ctx, Action(("list", "empty")));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("That pick list has no entries.");
        ctx.Variables.Should().NotContainKey("pick");
    }
}
