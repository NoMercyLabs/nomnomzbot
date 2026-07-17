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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Games;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the chat command gate implements the MAX rule (roles-permissions §0: "Effective level =
/// MAX(Twitch-badge role, bot-role grants, individual capability grants)" via §3.2
/// <see cref="IRoleResolver.ResolveEffectiveLevelAsync"/>) against the REAL <see cref="RoleResolver"/> over a
/// seeded database — not a mock of it: a badge-less Editor membership and an active <c>!permit</c> role grant
/// both clear a Moderator-floor command (previously badge-only resolution treated them as plebs), an expired
/// grant does NOT, badge-only moderators keep working, and the badge-sufficient hot path never touches the
/// resolver or the DB.
/// </summary>
public sealed class ChatCommandEffectiveLevelTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0198a000-0000-7000-8000-00000000e001");
    private static readonly Guid ViewerUser = Guid.Parse("0198a000-0000-7000-8000-00000000e002");
    private static readonly DateTime Now = new(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc);

    private const string TwitchUserId = "tw-viewer-9";
    private const string CommandKey = "modcmd";
    private const string CommandResponse = "granted!";
    private const int ModeratorFloor = 10;

    // ── the MAX rule: membership / permit elevation without a badge ────────────

    [Fact]
    public async Task Badgeless_editor_membership_clears_a_moderator_floor_command()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.ChannelMemberships.Add(
            new ChannelMembership
            {
                BroadcasterId = Broadcaster,
                UserId = ViewerUser,
                ManagementRole = ManagementRole.Editor,
                LevelValue = ManagementRole.Editor.ToLevel(),
                Source = MembershipSource.BotGrant,
                GrantedAt = Now,
            }
        );
        await db.SaveChangesAsync();

        (ChatMessageHandler sut, IChatProvider chat, _, _) = Build(db);

        await sut.HandleAsync(BadgelessEvent($"!{CommandKey}"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(Broadcaster, "msg-9", CommandResponse, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Active_permit_role_grant_clears_a_moderator_floor_command()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.PermitGrants.Add(
            new PermitGrant
            {
                BroadcasterId = Broadcaster,
                UserId = ViewerUser,
                GrantType = PermitGrantType.Role,
                GrantedRole = ManagementRole.Moderator,
                GrantedByUserId = Guid.NewGuid(),
                ExpiresAt = Now.AddHours(1), // active at the pinned clock
            }
        );
        await db.SaveChangesAsync();

        (ChatMessageHandler sut, IChatProvider chat, _, _) = Build(db);

        await sut.HandleAsync(BadgelessEvent($"!{CommandKey}"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(Broadcaster, "msg-9", CommandResponse, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Expired_permit_grant_no_longer_elevates_and_the_command_is_denied()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.PermitGrants.Add(
            new PermitGrant
            {
                BroadcasterId = Broadcaster,
                UserId = ViewerUser,
                GrantType = PermitGrantType.Role,
                GrantedRole = ManagementRole.Moderator,
                GrantedByUserId = Guid.NewGuid(),
                ExpiresAt = Now.AddHours(-1), // already expired at the pinned clock
            }
        );
        await db.SaveChangesAsync();

        (ChatMessageHandler sut, IChatProvider chat, _, _) = Build(db);

        await sut.HandleAsync(BadgelessEvent($"!{CommandKey}"), CancellationToken.None);

        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        await chat.DidNotReceiveWithAnyArgs().SendReplyAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Plain_viewer_with_no_grants_is_denied_a_moderator_floor_command()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        (ChatMessageHandler sut, IChatProvider chat, _, _) = Build(db);

        await sut.HandleAsync(BadgelessEvent($"!{CommandKey}"), CancellationToken.None);

        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        await chat.DidNotReceiveWithAnyArgs().SendReplyAsync(default, default!, default!, default);
    }

    // ── no regression + the hot-path short-circuit ─────────────────────────────

    [Fact]
    public async Task Badge_moderator_passes_without_any_resolver_or_user_lookup()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        (ChatMessageHandler sut, IChatProvider chat, IUserService users, IRoleResolver resolver) =
            Build(db);

        await sut.HandleAsync(ModeratorEvent($"!{CommandKey}"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(Broadcaster, "msg-10", CommandResponse, Arg.Any<CancellationToken>());
        // The short-circuit: the badge already met the floor, so the DB seam was never touched.
        await users
            .DidNotReceiveWithAnyArgs()
            .GetOrCreateAsync(default!, default!, default!, default!, default);
        await resolver.DidNotReceiveWithAnyArgs().ResolveEffectiveLevelAsync(default, default);
    }

    [Fact]
    public async Task Everyone_floor_command_from_a_plain_viewer_never_touches_the_resolver()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        (ChatMessageHandler sut, IChatProvider chat, IUserService users, IRoleResolver resolver) =
            Build(db, minPermissionLevel: 0);

        await sut.HandleAsync(BadgelessEvent($"!{CommandKey}"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(Broadcaster, "msg-9", CommandResponse, Arg.Any<CancellationToken>());
        await users
            .DidNotReceiveWithAnyArgs()
            .GetOrCreateAsync(default!, default!, default!, default!, default);
        await resolver.DidNotReceiveWithAnyArgs().ResolveEffectiveLevelAsync(default, default);
    }

    // ── shared scaffolding ──────────────────────────────────────────────────

    /// <summary>
    /// Handler over a REAL <see cref="RoleResolver"/> (pinned clock) reading <paramref name="db"/>, behind a
    /// real DI scope factory — the same seam the production handler resolves. <c>IUserService</c> is a
    /// recording stub mapping the Twitch id to the internal <see cref="ViewerUser"/> (identity mapping is not
    /// the behavior under test; the ladder resolution is). The channel carries one Moderator-floor template
    /// command whose sent response is the observable consequence.
    /// </summary>
    private static (
        ChatMessageHandler Sut,
        IChatProvider Chat,
        IUserService Users,
        IRoleResolver Resolver
    ) Build(AuthDbContext db, int minPermissionLevel = ModeratorFloor)
    {
        ChannelContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-888",
            ChannelName = "stoney_eagle",
        };
        ctx.Commands[CommandKey] = new CachedCommand
        {
            Name = CommandKey,
            TemplateResponses = [CommandResponse],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = minPermissionLevel,
            Tier = "template",
        };

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);

        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                TwitchUserId,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(ViewerUser.ToString(), "viewer", "Viewer", null, null, Now, Now)
                )
            );

        FakeTimeProvider clock = new(Now);
        // A spy over the REAL resolver: forwards to RoleResolver(db) so the outcome is the true ladder
        // resolution, while NSubstitute still counts calls for the short-circuit assertions.
        RoleResolver realResolver = new(db, clock);
        IRoleResolver resolver = Substitute.For<IRoleResolver>();
        resolver
            .ResolveEffectiveLevelAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo =>
                realResolver.ResolveEffectiveLevelAsync(
                    callInfo.ArgAt<Guid>(0),
                    callInfo.ArgAt<Guid>(1),
                    callInfo.ArgAt<CancellationToken>(2)
                )
            );

        ServiceCollection services = new();
        services.AddSingleton<IUserService>(users);
        services.AddSingleton<IRoleResolver>(resolver);
        ServiceProvider provider = services.BuildServiceProvider();

        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => callInfo.ArgAt<string>(0));

        IChatProvider chat = Substitute.For<IChatProvider>();

        ChatMessageHandler sut = new(
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ICooldownManager>(),
            chat,
            Substitute.For<IPipelineEngine>(),
            Substitute.For<IBuiltinCommandCatalog>(),
            templates,
            Substitute.For<IEventBus>(),
            new LiveGameSessionRegistry(),
            clock,
            NullLogger<ChatMessageHandler>.Instance
        );

        return (sut, chat, users, resolver);
    }

    private static ChatMessageReceivedEvent BadgelessEvent(string message) =>
        new()
        {
            BroadcasterId = Broadcaster,
            MessageId = "msg-9",
            TwitchBroadcasterId = "tw-888",
            UserId = TwitchUserId,
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = message,
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    private static ChatMessageReceivedEvent ModeratorEvent(string message) =>
        new()
        {
            BroadcasterId = Broadcaster,
            MessageId = "msg-10",
            TwitchBroadcasterId = "tw-888",
            UserId = TwitchUserId,
            UserDisplayName = "Moddy",
            UserLogin = "moddy",
            Message = message,
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = true,
            IsBroadcaster = false,
        };
}
