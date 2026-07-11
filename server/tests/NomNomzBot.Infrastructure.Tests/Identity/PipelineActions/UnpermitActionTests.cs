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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Identity.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity.PipelineActions;

/// <summary>
/// Proves the <c>unpermit</c> pipeline action resolves the target, enforces <c>permit:issue</c> in-action, and
/// delegates to <see cref="IPermitService.RevokeAsync"/> with the right selector — a named role/capability token,
/// or <c>null</c> (revoke ALL) when no token is given. The refusal case asserts nothing is revoked.
/// </summary>
public sealed class UnpermitActionTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private static readonly Guid InvokerGuid = Guid.Parse("0192a000-0000-7000-8000-0000000000c2");
    private static readonly Guid TargetGuid = Guid.Parse("0192a000-0000-7000-8000-0000000000c3");
    private const string InvokerTwitchId = "111";
    private const string TargetTwitchId = "222";

    [Fact]
    public async Task Revokes_the_named_selector()
    {
        Harness h = new();
        UnpermitAction sut = h.Action();

        ActionResult result = await sut.ExecuteAsync(
            Ctx(("target.id", TargetTwitchId), ("target.display", "Streamer"), ("args.1", "mod")),
            new ActionDefinition { Type = "unpermit" }
        );

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Contain("mod").And.Contain("Streamer");
        await h
            .Permits.Received(1)
            .RevokeAsync(Broadcaster, TargetGuid, "mod", InvokerGuid, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revokes_all_active_grants_when_no_selector_is_given()
    {
        Harness h = new();
        UnpermitAction sut = h.Action();

        ActionResult result = await sut.ExecuteAsync(
            Ctx(("target.id", TargetTwitchId)),
            new ActionDefinition { Type = "unpermit" }
        );

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Contain("all");
        // A null selector is the "revoke everything" contract (§3.6) — assert it reaches the service as null.
        await h
            .Permits.Received(1)
            .RevokeAsync(Broadcaster, TargetGuid, null, InvokerGuid, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_a_caller_without_permit_issue_and_revokes_nothing()
    {
        Harness h = new();
        h.Roles.HasCapabilityAsync(
                InvokerGuid,
                Broadcaster,
                "permit:issue",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(false));
        UnpermitAction sut = h.Action();

        ActionResult result = await sut.ExecuteAsync(
            Ctx(("target.id", TargetTwitchId), ("args.1", "mod")),
            new ActionDefinition { Type = "unpermit" }
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("permit:issue");
        await h.Permits.DidNotReceiveWithAnyArgs().RevokeAsync(default, default, default, default);
    }

    private static PipelineExecutionContext Ctx(params (string Key, string Value)[] vars)
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TriggeredByUserId = InvokerTwitchId,
            TriggeredByDisplayName = "Invoker",
            MessageId = "m1",
            RawMessage = "!unpermit",
        };
        ctx.Variables["user.name"] = "invoker";
        foreach ((string key, string value) in vars)
            ctx.Variables[key] = value;
        return ctx;
    }

    private sealed class Harness
    {
        public IPermitService Permits { get; } = Substitute.For<IPermitService>();
        public IUserService Users { get; } = Substitute.For<IUserService>();
        public IRoleResolver Roles { get; } = Substitute.For<IRoleResolver>();

        public Harness()
        {
            Users
                .GetOrCreateAsync(
                    InvokerTwitchId,
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Result.Success(UserDtoFor(InvokerGuid)));
            Users
                .GetOrCreateAsync(
                    TargetTwitchId,
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Result.Success(UserDtoFor(TargetGuid)));
            Roles
                .HasCapabilityAsync(
                    InvokerGuid,
                    Broadcaster,
                    "permit:issue",
                    Arg.Any<CancellationToken>()
                )
                .Returns(Result.Success(true));
            Permits
                .RevokeAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<Guid>(),
                    Arg.Any<string?>(),
                    Arg.Any<Guid>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Result.Success());
        }

        public UnpermitAction Action() => new(Permits, Users, Roles);
    }

    private static UserDto UserDtoFor(Guid id) =>
        new(id.ToString(), "user", "User", null, null, default, default);
}
