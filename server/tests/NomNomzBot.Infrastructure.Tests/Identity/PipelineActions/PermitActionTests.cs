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
using Microsoft.Extensions.Time.Testing;
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
/// Proves the <c>permit</c> pipeline action resolves the invoker + target to platform user Guids, enforces the
/// <c>permit:issue</c> Gate-2 in-action, and delegates to <see cref="IPermitService"/> with the correct arguments —
/// a role grant when the token names a management role, a capability grant otherwise, with the parsed expiry. The
/// negative cases assert nothing is granted (the security consequence), not merely that a call returned.
/// </summary>
public sealed class PermitActionTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000000b1");
    private static readonly Guid InvokerGuid = Guid.Parse("0192a000-0000-7000-8000-0000000000a1");
    private static readonly Guid TargetGuid = Guid.Parse("0192a000-0000-7000-8000-0000000000a2");
    private const string InvokerTwitchId = "111";
    private const string TargetTwitchId = "222";

    [Fact]
    public async Task Permits_a_management_role_when_the_token_names_a_role()
    {
        Harness h = new();
        PermitAction sut = h.Action();

        ActionResult result = await sut.ExecuteAsync(
            Ctx(("target.id", TargetTwitchId), ("target.display", "Streamer"), ("args.1", "mod")),
            new ActionDefinition { Type = "permit" }
        );

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Contain("Moderator").And.Contain("Streamer");
        // The grant actually went through the permit service as a ROLE grant, resolved to the target's user Guid,
        // issued by the invoker's user Guid, with no expiry — never a capability grant.
        await h
            .Permits.Received(1)
            .GrantRoleAsync(
                Broadcaster,
                TargetGuid,
                ManagementRole.Moderator,
                InvokerGuid,
                null,
                "!permit",
                Arg.Any<CancellationToken>()
            );
        await h
            .Permits.DidNotReceiveWithAnyArgs()
            .GrantCapabilityAsync(default, default, default!, default, default, default);
    }

    [Fact]
    public async Task Permits_a_capability_when_the_token_is_an_action_key()
    {
        Harness h = new();
        PermitAction sut = h.Action();

        ActionResult result = await sut.ExecuteAsync(
            Ctx(("target.id", TargetTwitchId), ("args.1", "economy:transfer:write")),
            new ActionDefinition { Type = "permit" }
        );

        result.Succeeded.Should().BeTrue();
        await h
            .Permits.Received(1)
            .GrantCapabilityAsync(
                Broadcaster,
                TargetGuid,
                "economy:transfer:write",
                InvokerGuid,
                null,
                "!permit",
                Arg.Any<CancellationToken>()
            );
        await h
            .Permits.DidNotReceiveWithAnyArgs()
            .GrantRoleAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task Refuses_a_caller_without_permit_issue_and_grants_nothing()
    {
        Harness h = new();
        h.Roles.HasCapabilityAsync(
                InvokerGuid,
                Broadcaster,
                "permit:issue",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(false));
        PermitAction sut = h.Action();

        ActionResult result = await sut.ExecuteAsync(
            Ctx(("target.id", TargetTwitchId), ("args.1", "mod")),
            new ActionDefinition { Type = "permit" }
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("permit:issue");
        await h
            .Permits.DidNotReceiveWithAnyArgs()
            .GrantRoleAsync(default, default, default, default, default, default);
        await h
            .Permits.DidNotReceiveWithAnyArgs()
            .GrantCapabilityAsync(default, default, default!, default, default, default);
    }

    [Fact]
    public async Task Fails_when_no_target_is_resolved_and_grants_nothing()
    {
        Harness h = new();
        PermitAction sut = h.Action();

        // No target.id variable (no @mention resolved) — the action must not guess a target.
        ActionResult result = await sut.ExecuteAsync(
            Ctx(("args.1", "mod")),
            new ActionDefinition { Type = "permit" }
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("target");
        await h
            .Permits.DidNotReceiveWithAnyArgs()
            .GrantRoleAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task Duration_minutes_sets_an_expiry_relative_to_now()
    {
        Harness h = new();
        PermitAction sut = h.Action();
        DateTime expected = h.Clock.GetUtcNow().UtcDateTime.AddMinutes(30);

        ActionResult result = await sut.ExecuteAsync(
            Ctx(("target.id", TargetTwitchId), ("args.1", "mod")),
            new ActionDefinition
            {
                Type = "permit",
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["duration_minutes"] = JsonSerializer.SerializeToElement(30),
                },
            }
        );

        result.Succeeded.Should().BeTrue();
        await h
            .Permits.Received(1)
            .GrantRoleAsync(
                Broadcaster,
                TargetGuid,
                ManagementRole.Moderator,
                InvokerGuid,
                expected,
                "!permit",
                Arg.Any<CancellationToken>()
            );
    }

    private static PipelineExecutionContext Ctx(params (string Key, string Value)[] vars)
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TriggeredByUserId = InvokerTwitchId,
            TriggeredByDisplayName = "Invoker",
            MessageId = "m1",
            RawMessage = "!permit",
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
        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-07-05T12:00:00Z"));

        public Harness()
        {
            Users
                .GetOrCreateAsync(
                    InvokerTwitchId,
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
                .GrantRoleAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<Guid>(),
                    Arg.Any<ManagementRole>(),
                    Arg.Any<Guid>(),
                    Arg.Any<DateTime?>(),
                    Arg.Any<string?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Result.Success(SampleGrant()));
            Permits
                .GrantCapabilityAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<Guid>(),
                    Arg.Any<DateTime?>(),
                    Arg.Any<string?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Result.Success(SampleGrant()));
        }

        public PermitAction Action() => new(Permits, Users, Roles, Clock);
    }

    private static UserDto UserDtoFor(Guid id) =>
        new(id.ToString(), "user", "User", null, null, default, default);

    private static PermitGrantDto SampleGrant() =>
        new(
            Guid.Parse("0192a000-0000-7000-8000-0000000000f1"),
            TargetGuid,
            null,
            PermitGrantType.Role,
            ManagementRole.Moderator,
            null,
            InvokerGuid,
            null,
            null,
            "!permit",
            default
        );
}
