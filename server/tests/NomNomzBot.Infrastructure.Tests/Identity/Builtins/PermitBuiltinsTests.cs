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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity.Builtins;

/// <summary>
/// Proves the zero-config <c>!permit</c>/<c>!unpermit</c> chat surface (BUILD item 24b): a permitted
/// invoker's role token grants a ROLE and a capability token grants a CAPABILITY (with the optional
/// minutes → expiry), an invoker without <c>permit:issue</c> is refused with NO grant made, an unknown
/// @mention fails honestly, and an unselective <c>!unpermit</c> revokes everything.
/// </summary>
public sealed class PermitBuiltinsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000b1");
    private static readonly Guid InvokerGuid = Guid.Parse("0192a000-0000-7000-8000-0000000000b2");
    private static readonly Guid TargetGuid = Guid.Parse("0192a000-0000-7000-8000-0000000000b3");
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 18, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        PermitBuiltin Permit,
        UnpermitBuiltin Unpermit,
        IPermitService Permits,
        IRoleResolver Roles
    );

    private static Harness Build(bool mayIssue = true, bool targetExists = true)
    {
        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                "tw-invoker",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Dto(InvokerGuid, "modlogin")));
        users
            .GetOrCreateAsync(
                "tw-target",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Dto(TargetGuid, "someone")));

        IRoleResolver roles = Substitute.For<IRoleResolver>();
        roles
            .HasCapabilityAsync(InvokerGuid, Channel, "permit:issue", Arg.Any<CancellationToken>())
            .Returns(Result.Success(mayIssue));

        ITwitchUsersApi twitchUsers = Substitute.For<ITwitchUsersApi>();
        twitchUsers
            .GetUsersByLoginsAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Contains("someone")),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                targetExists
                    ? Result.Success<IReadOnlyList<TwitchUser>>([
                        new TwitchUser(
                            "tw-target",
                            "someone",
                            "Someone",
                            "",
                            "",
                            "",
                            "",
                            "",
                            0,
                            Now
                        ),
                    ])
                    : Result.Success<IReadOnlyList<TwitchUser>>([])
            );

        IPermitService permits = Substitute.For<IPermitService>();
        permits
            .GrantRoleAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<ManagementRole>(),
                Arg.Any<Guid>(),
                Arg.Any<DateTime?>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Grant()));
        permits
            .GrantCapabilityAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<DateTime?>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Grant()));
        permits
            .RevokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        FakeTimeProvider clock = new(Now);
        return new Harness(
            new PermitBuiltin(permits, users, roles, twitchUsers, clock),
            new UnpermitBuiltin(permits, users, roles, twitchUsers),
            permits,
            roles
        );
    }

    private static BuiltinCommandContext Context(string args) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = "tw-invoker",
            TriggeringUserLogin = "modlogin",
            TriggeringUserDisplayName = "ModName",
            RoleLevel = 2,
            Args = args,
        };

    [Fact]
    public async Task A_role_token_grants_the_role_with_the_minutes_expiry()
    {
        Harness h = Build();

        Result<string> reply = await h.Permit.ExecuteAsync(Context("@Someone mod 30"));

        reply.Value.Should().Contain("Moderator").And.Contain("Someone");
        await h
            .Permits.Received(1)
            .GrantRoleAsync(
                Channel,
                TargetGuid,
                ManagementRole.Moderator,
                InvokerGuid,
                Now.UtcDateTime.AddMinutes(30),
                "!permit",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_capability_token_grants_the_capability_permanently_when_no_minutes_given()
    {
        Harness h = Build();

        Result<string> reply = await h.Permit.ExecuteAsync(Context("@someone channel:title:write"));

        reply.Value.Should().Contain("channel:title:write");
        await h
            .Permits.Received(1)
            .GrantCapabilityAsync(
                Channel,
                TargetGuid,
                "channel:title:write",
                InvokerGuid,
                null,
                "!permit",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_invoker_without_permit_issue_is_refused_and_nothing_is_granted()
    {
        Harness h = Build(mayIssue: false);

        Result<string> reply = await h.Permit.ExecuteAsync(Context("@someone mod"));

        reply.Value.Should().Contain("permit:issue");
        await h
            .Permits.DidNotReceiveWithAnyArgs()
            .GrantRoleAsync(
                default,
                default,
                default,
                default,
                default,
                default!,
                Arg.Any<CancellationToken>()
            );
        await h
            .Permits.DidNotReceiveWithAnyArgs()
            .GrantCapabilityAsync(
                default,
                default,
                default!,
                default,
                default,
                default!,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_unknown_mention_fails_honestly_with_no_grant()
    {
        Harness h = Build(targetExists: false);

        Result<string> reply = await h.Permit.ExecuteAsync(Context("@someone mod"));

        reply.Value.Should().Contain("not found");
        await h
            .Permits.DidNotReceiveWithAnyArgs()
            .GrantRoleAsync(
                default,
                default,
                default,
                default,
                default,
                default!,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Unpermit_without_a_selector_revokes_all_grants()
    {
        Harness h = Build();

        Result<string> reply = await h.Unpermit.ExecuteAsync(Context("@someone"));

        reply.Value.Should().Contain("all permits");
        await h
            .Permits.Received(1)
            .RevokeAsync(Channel, TargetGuid, null, InvokerGuid, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unpermit_with_a_selector_revokes_exactly_that_grant()
    {
        Harness h = Build();

        Result<string> reply = await h.Unpermit.ExecuteAsync(
            Context("@someone channel:title:write")
        );

        reply.Value.Should().Contain("channel:title:write");
        await h
            .Permits.Received(1)
            .RevokeAsync(
                Channel,
                TargetGuid,
                "channel:title:write",
                InvokerGuid,
                Arg.Any<CancellationToken>()
            );
    }

    private static PermitGrantDto Grant() =>
        new(
            Guid.CreateVersion7(),
            TargetGuid,
            "someone",
            PermitGrantType.Capability,
            null,
            "channel:title:write",
            InvokerGuid,
            null,
            null,
            "!permit",
            Now.UtcDateTime
        );

    private static UserDto Dto(Guid id, string login) =>
        new(
            Id: id.ToString(),
            Username: login,
            DisplayName: login,
            ProfileImageUrl: null,
            Email: null,
            CreatedAt: DateTime.UnixEpoch,
            LastLoginAt: DateTime.UnixEpoch
        );
}
