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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PickLists.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.PickLists.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.PickLists;
using NomNomzBot.Infrastructure.PickLists.PipelineActions;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using Xunit;
using ActionDefinition = NomNomzBot.Application.Abstractions.Pipeline.ActionDefinition;

namespace NomNomzBot.Infrastructure.Tests.PickLists;

/// <summary>
/// Proves the <c>pick_from_list</c> pipeline action end to end through the REAL
/// <see cref="TemplateResolver"/> over the REAL <see cref="PickListService"/>: it draws one entry from the
/// named list, resolves the entry's own placeholders (<c>{user}</c>, nested <c>{list.pick.*}</c>) against the
/// pipeline's variables BEFORE storing — so the stored variable is one fully-resolved roll that every later
/// step reads verbatim — and it fails loudly, without touching the variables, on a missing <c>list</c> param
/// or a service rejection (unknown/empty list). A missing NESTED list resolves to empty, never to the raw
/// <c>{list.pick.…}</c> token.
/// </summary>
public sealed class PickFromListActionTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("019f2a00-3333-7000-8000-000000000001");

    private readonly PickListSqliteTestDatabase _database;
    private readonly PickListTestDbContext _db;
    private readonly PickListService _lists;
    private readonly TemplateResolver _resolver;
    private readonly PickFromListAction _action;

    public PickFromListActionTests()
    {
        _database = PickListSqliteTestDatabase.Open();
        _db = _database.NewContext();

        _db.Channels.Add(
            new Channel
            {
                Id = Channel,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "555",
                Name = "streamer",
                NameNormalized = "streamer",
            }
        );
        AddList("attacks", ["{user} bonks {list.pick.adjectives} {target}"]);
        AddList("adjectives", ["mighty"]);
        AddList("broken", ["A[{list.pick.nope}]B"]);
        AddList("greetings", ["hi there"]);
        _db.SaveChanges();

        RecordingEventBus bus = new();
        _lists = new PickListService(_db, bus);

        // The resolver resolves IPickListService from a fresh scope per call; register the focused SQLite
        // context as the singleton IApplicationDbContext the scoped service reads through (mirrors
        // PickListTemplateResolverTests).
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(_db);
        services.AddSingleton<IEventBus>(bus);
        services.AddScoped<IPickListService, PickListService>();
        ServiceProvider provider = services.BuildServiceProvider();
        _resolver = new TemplateResolver(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );

        _action = new PickFromListAction(_lists, _resolver);
    }

    private void AddList(string name, List<string> items) =>
        _db.PickLists.Add(
            new PickList
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Channel,
                Name = name,
                Items = items,
            }
        );

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
    public async Task Picked_entry_is_fully_resolved_against_ctx_variables_before_storing()
    {
        PipelineExecutionContext ctx = Context();
        ctx.Variables["user"] = "alice";
        ctx.Variables["target"] = "bob";

        ActionResult result = await _action.ExecuteAsync(ctx, Action(("list", "attacks")));

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        // {user}/{target} came from the pipeline variables; the nested {list.pick.adjectives} rolled too.
        ctx.Variables["pick"].Should().Be("alice bonks mighty bob");
    }

    [Fact]
    public async Task Later_steps_read_the_same_resolved_string_verbatim()
    {
        PipelineExecutionContext ctx = Context();
        ctx.Variables["user"] = "alice";
        ctx.Variables["target"] = "bob";
        await _action.ExecuteAsync(ctx, Action(("list", "attacks")));

        // The engine's substitution is single-pass: whatever a later step substitutes for {pick} is used
        // as-is. Since the pick was stored fully resolved, that single pass yields the finished text.
        string laterStep = _resolver.Resolve("and then {pick}!", ctx.Variables);

        laterStep.Should().Be("and then alice bonks mighty bob!");
    }

    [Fact]
    public async Task Missing_nested_list_resolves_to_empty_never_the_raw_token()
    {
        PipelineExecutionContext ctx = Context();

        ActionResult result = await _action.ExecuteAsync(ctx, Action(("list", "broken")));

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        ctx.Variables["pick"].Should().Be("A[]B");
        ctx.Variables["pick"].Should().NotContain("{list.pick");
    }

    [Fact]
    public async Task Stores_into_a_custom_variable_name()
    {
        PipelineExecutionContext ctx = Context();

        await _action.ExecuteAsync(ctx, Action(("list", "greetings"), ("variable", "greeting")));

        ctx.Variables["greeting"].Should().Be("hi there");
    }

    [Fact]
    public async Task Missing_list_fails_without_picking()
    {
        IPickListService lists = Substitute.For<IPickListService>();
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        PickFromListAction action = new(lists, resolver);

        ActionResult result = await action.ExecuteAsync(Context(), Action());

        result.Succeeded.Should().BeFalse();
        await lists
            .DidNotReceive()
            .PickRandomAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_list_surfaces_the_service_reason_and_resolves_nothing()
    {
        IPickListService lists = Substitute.For<IPickListService>();
        lists
            .PickRandomAsync(Channel, "empty", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<string>("That pick list has no entries.", "PICKLIST_EMPTY"));
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        PickFromListAction action = new(lists, resolver);
        PipelineExecutionContext ctx = Context();

        ActionResult result = await action.ExecuteAsync(ctx, Action(("list", "empty")));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("That pick list has no entries.");
        ctx.Variables.Should().NotContainKey("pick");
        await resolver
            .DidNotReceive()
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Unknown_list_fails_typed_through_the_real_service()
    {
        PipelineExecutionContext ctx = Context();

        ActionResult result = await _action.ExecuteAsync(ctx, Action(("list", "nonexistent")));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        ctx.Variables.Should().NotContainKey("pick");
    }

    public void Dispose()
    {
        _db.Dispose();
        _database.Dispose();
    }
}
