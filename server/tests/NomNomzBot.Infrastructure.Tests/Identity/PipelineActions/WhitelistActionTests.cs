// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity.PipelineActions;

/// <summary>
/// Proves <c>whitelist</c> / <c>unwhitelist</c> resolve the invoker and target to platform user Guids, enforce
/// the broadcaster-only floor IN-ACTION (a pipeline carries no <c>[RequireAction]</c> floor of its own), and
/// delegate to <see cref="ICommunityStandingService"/> — never writing a standing when the invoker's resolved
/// level is below the broadcaster rung.
/// </summary>
public sealed class WhitelistActionTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private static readonly Guid InvokerGuid = Guid.Parse("0192a000-0000-7000-8000-0000000000c2");
    private static readonly Guid TargetGuid = Guid.Parse("0192a000-0000-7000-8000-0000000000c3");
    private const string InvokerTwitchId = "111";
    private const string TargetTwitchId = "222";

    [Fact]
    public async Task Whitelist_grants_the_resolved_standing_when_the_invoker_is_the_broadcaster()
    {
        Harness h = new();
        WhitelistAction sut = h.Action();

        ActionResult result = await sut.ExecuteAsync(
            Ctx(("target.id", TargetTwitchId)),
            Def("vip")
        );

        result.Succeeded.Should().BeTrue();
        await h
            .Standings.Received(1)
            .SetManualStandingAsync(
                Broadcaster,
                TargetGuid,
                CommunityStanding.Vip,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Whitelist_refuses_a_non_broadcaster_invoker_and_grants_nothing()
    {
        Harness h = new();
        h.Roles.ResolveEffectiveLevelAsync(InvokerGuid, Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success(10)); // Moderator rung — below the broadcaster floor (40)
        WhitelistAction sut = h.Action();

        ActionResult result = await sut.ExecuteAsync(
            Ctx(("target.id", TargetTwitchId)),
            Def("moderator")
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("broadcaster");
        await h
            .Standings.DidNotReceiveWithAnyArgs()
            .SetManualStandingAsync(default, default, default);
    }

    [Fact]
    public async Task Whitelist_fails_when_no_target_is_resolved_and_grants_nothing()
    {
        Harness h = new();
        WhitelistAction sut = h.Action();

        ActionResult result = await sut.ExecuteAsync(Ctx(), Def("vip"));

        result.Succeeded.Should().BeFalse();
        await h
            .Standings.DidNotReceiveWithAnyArgs()
            .SetManualStandingAsync(default, default, default);
    }

    [Fact]
    public async Task Unwhitelist_removes_the_targets_override_when_the_invoker_is_the_broadcaster()
    {
        Harness h = new();
        h.Standings.RemoveManualStandingAsync(Broadcaster, TargetGuid, Arg.Any<CancellationToken>())
            .Returns(Result.Success(true));
        UnwhitelistAction sut = h.UnwhitelistAction();

        ActionResult result = await sut.ExecuteAsync(
            Ctx(("target.id", TargetTwitchId)),
            new() { Type = "unwhitelist" }
        );

        result.Succeeded.Should().BeTrue();
        await h
            .Standings.Received(1)
            .RemoveManualStandingAsync(Broadcaster, TargetGuid, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unwhitelist_refuses_a_non_broadcaster_invoker_and_removes_nothing()
    {
        Harness h = new();
        h.Roles.ResolveEffectiveLevelAsync(InvokerGuid, Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success(10));
        UnwhitelistAction sut = h.UnwhitelistAction();

        ActionResult result = await sut.ExecuteAsync(
            Ctx(("target.id", TargetTwitchId)),
            new() { Type = "unwhitelist" }
        );

        result.Succeeded.Should().BeFalse();
        await h.Standings.DidNotReceiveWithAnyArgs().RemoveManualStandingAsync(default, default);
    }

    private static ActionDefinition Def(string standing) =>
        new()
        {
            Type = "whitelist",
            Parameters = new() { ["standing"] = JsonSerializer.SerializeToElement(standing) },
        };

    private static PipelineExecutionContext Ctx(params (string Key, string Value)[] vars)
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TriggeredByUserId = InvokerTwitchId,
            TriggeredByDisplayName = "Invoker",
            MessageId = "m1",
            RawMessage = "!whitelist",
        };
        foreach ((string key, string value) in vars)
            ctx.Variables[key] = value;
        return ctx;
    }

    private sealed class Harness
    {
        public ICommunityStandingService Standings { get; } =
            Substitute.For<ICommunityStandingService>();
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
                .ResolveEffectiveLevelAsync(InvokerGuid, Broadcaster, Arg.Any<CancellationToken>())
                .Returns(Result.Success(40)); // Broadcaster rung
            Standings
                .SetManualStandingAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<Guid>(),
                    Arg.Any<CommunityStanding>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Result.Success());
        }

        public WhitelistAction Action() => new(Standings, Users, Roles);

        public UnwhitelistAction UnwhitelistAction() => new(Standings, Users, Roles);
    }

    private static UserDto UserDtoFor(Guid id) =>
        new(id.ToString(), "user", "User", null, null, default, default);
}
