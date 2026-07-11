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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.ViewerData.Entities;
using NomNomzBot.Infrastructure.ViewerData;
using NomNomzBot.Infrastructure.ViewerData.PipelineActions;
using NSubstitute;
// Disambiguate from the Gate-2 ActionDefinition entity (Domain.Identity.Entities).
using ActionDefinition = NomNomzBot.Application.Abstractions.Pipeline.ActionDefinition;

namespace NomNomzBot.Infrastructure.Tests.ViewerData;

/// <summary>
/// Proves the <c>set_viewer_data</c>/<c>adjust_viewer_data</c> pipeline actions (per-viewer-data.md §4):
/// writes land on the RIGHT viewer (triggering viewer by default; <c>target</c> accepts a variable
/// reference, an @login of a known local user, or a platform Guid), values template-resolve, the fresh
/// value is published into <c>{viewer.data.&lt;key&gt;}</c> for the rest of the run, and unknown targets
/// fail honestly with no write.
/// </summary>
public sealed class ViewerDataActionsTests
{
    private static readonly Guid Channel = Guid.Parse("0192b100-0000-7000-8000-00000000c001");
    private static readonly Guid Alice = Guid.Parse("0192b100-0000-7000-8000-00000000a001");
    private static readonly Guid Bob = Guid.Parse("0192b100-0000-7000-8000-00000000a002");

    private readonly ViewerDataTestDbContext _db;
    private readonly ViewerDataService _service;
    private readonly IUserService _users;
    private readonly ITemplateResolver _templates;

    public ViewerDataActionsTests()
    {
        _db = ViewerDataTestDbContext.New();
        _db.Users.Add(
            new User
            {
                Id = Alice,
                TwitchUserId = "111",
                Username = "alice",
                UsernameNormalized = "alice",
                DisplayName = "Alice",
            }
        );
        _db.Users.Add(
            new User
            {
                Id = Bob,
                TwitchUserId = "222",
                Username = "bob",
                UsernameNormalized = "bob",
                DisplayName = "Bob",
            }
        );
        _db.SaveChanges();
        _service = new ViewerDataService(_db, TimeProvider.System);

        _users = Substitute.For<IUserService>();
        _users
            .GetOrCreateAsync(
                "111",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Dto(Alice, "alice", "Alice")));
        _users
            .GetOrCreateAsync(
                "222",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Dto(Bob, "bob", "Bob")));

        // Seed-only substitution — enough to prove the value parameter template-resolves.
        _templates = Substitute.For<ITemplateResolver>();
        _templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string template = call.ArgAt<string>(0);
                foreach (
                    KeyValuePair<string, string> kvp in call.ArgAt<IDictionary<string, string>>(1)
                )
                    template = template.Replace($"{{{kvp.Key}}}", kvp.Value);
                return Task.FromResult(template);
            });
    }

    private static UserDto Dto(Guid id, string login, string display) =>
        new(id.ToString(), login, display, null, null, default, default);

    private static PipelineExecutionContext Context(
        string triggeredBy = "111",
        params (string Key, string Value)[] vars
    )
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = triggeredBy,
            TriggeredByDisplayName = "Alice",
            MessageId = "m1",
            RawMessage = "!cmd",
            CancellationToken = default,
        };
        foreach ((string key, string value) in vars)
            ctx.Variables[key] = value;
        return ctx;
    }

    private static ActionDefinition Action(string type, params (string Key, object Value)[] p) =>
        new()
        {
            Type = type,
            Parameters = p.ToDictionary(
                x => x.Key,
                x => JsonSerializer.SerializeToElement(x.Value)
            ),
        };

    [Fact]
    public async Task SetViewerData_WritesForTheTriggeringViewer_AndTemplateResolvesTheValue()
    {
        SetViewerDataAction sut = new(_service, _users, _db, _templates);
        PipelineExecutionContext ctx = Context(vars: ("args.0", "DOOM"));

        ActionResult result = await sut.ExecuteAsync(
            ctx,
            Action("set_viewer_data", ("key", "favorite_game"), ("value", "{args.0}"))
        );

        result.Succeeded.Should().BeTrue();
        ViewerDatum row = await _db.ViewerData.SingleAsync();
        row.ViewerUserId.Should().Be(Alice);
        row.Key.Should().Be("favorite_game");
        row.Value.Should().Be("DOOM");
        ctx.Variables["viewer.data.favorite_game"].Should().Be("DOOM");
    }

    [Fact]
    public async Task AdjustViewerData_Increments_AndPublishesTheFreshValueIntoTheRun()
    {
        AdjustViewerDataAction sut = new(_service, _users, _db);
        PipelineExecutionContext ctx = Context();
        ActionDefinition action = Action("adjust_viewer_data", ("key", "deaths"), ("delta", "1"));

        await sut.ExecuteAsync(ctx, action);
        ActionResult second = await sut.ExecuteAsync(ctx, action);

        second.Succeeded.Should().BeTrue();
        second.Output.Should().Be("2");
        ctx.Variables["viewer.data.deaths"].Should().Be("2");
        (await _db.ViewerData.SingleAsync(d => d.Key == "deaths")).Value.Should().Be("2");
    }

    [Fact]
    public async Task Target_VariableReference_ResolvesTheMentionedViewer()
    {
        AdjustViewerDataAction sut = new(_service, _users, _db);
        PipelineExecutionContext ctx = Context(
            "111",
            ("target.id", "222"),
            ("target.name", "bob"),
            ("target", "bob")
        );

        ActionResult result = await sut.ExecuteAsync(
            ctx,
            Action(
                "adjust_viewer_data",
                ("key", "deaths"),
                ("delta", "5"),
                ("target", "{target.id}")
            )
        );

        result.Succeeded.Should().BeTrue();
        ViewerDatum row = await _db.ViewerData.SingleAsync();
        row.ViewerUserId.Should().Be(Bob);
        row.Value.Should().Be("5");
    }

    [Fact]
    public async Task Target_LoginOfAKnownLocalUser_Resolves()
    {
        SetViewerDataAction sut = new(_service, _users, _db, _templates);

        ActionResult result = await sut.ExecuteAsync(
            Context(),
            Action("set_viewer_data", ("key", "quest"), ("value", "done"), ("target", "@Bob"))
        );

        result.Succeeded.Should().BeTrue();
        (await _db.ViewerData.SingleAsync()).ViewerUserId.Should().Be(Bob);
    }

    [Fact]
    public async Task Target_UnknownLogin_FailsHonestly_WithNoWrite()
    {
        SetViewerDataAction sut = new(_service, _users, _db, _templates);

        ActionResult result = await sut.ExecuteAsync(
            Context(),
            Action("set_viewer_data", ("key", "quest"), ("value", "done"), ("target", "@ghost"))
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ghost");
        (await _db.ViewerData.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Target_PlatformGuid_IsUsedDirectly_WithoutAnIdentityLookup()
    {
        SetViewerDataAction sut = new(_service, _users, _db, _templates);

        ActionResult result = await sut.ExecuteAsync(
            Context(),
            Action(
                "set_viewer_data",
                ("key", "quest"),
                ("value", "done"),
                ("target", Bob.ToString())
            )
        );

        result.Succeeded.Should().BeTrue();
        (await _db.ViewerData.SingleAsync()).ViewerUserId.Should().Be(Bob);
        await _users
            .DidNotReceive()
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AdjustViewerData_NonNumericDelta_FailsWithoutWriting()
    {
        AdjustViewerDataAction sut = new(_service, _users, _db);

        ActionResult result = await sut.ExecuteAsync(
            Context(),
            Action("adjust_viewer_data", ("key", "deaths"), ("delta", "lots"))
        );

        result.Succeeded.Should().BeFalse();
        (await _db.ViewerData.CountAsync()).Should().Be(0);
    }
}
