// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// A built-in that reads <see cref="BuiltinCommandContext.RoleLevel"/> must see the EFFECTIVE level the
/// command gate already honors (badge, bot-granted role, <c>!permit</c>), not the bare badge level — and the
/// resolver is asked at most once per message. Runs the real <see cref="RoleResolver"/> over a seeded
/// database; the observable consequence is the context the built-in receives.
/// </summary>
public sealed class BuiltinEffectiveRoleTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0198a000-0000-7000-8000-00000000f001");
    private static readonly Guid ViewerUser = Guid.Parse("0198a000-0000-7000-8000-00000000f002");
    private static readonly DateTime Now = new(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc);

    private const string TwitchUserId = "tw-viewer-7";
    private const string BuiltinKey = "probe";
    private const int ModeratorFloor = 10;

    [Fact]
    public async Task Badgeless_editor_gets_the_editor_level_in_a_moderator_floor_builtin()
    {
        AuthDbContext db = await EditorDbAsync();
        Harness h = Build(db, builtinFloor: ModeratorFloor);

        await h.Sut.HandleAsync(
            Event($"!{BuiltinKey}", broadcaster: false),
            CancellationToken.None
        );

        BuiltinCommandContext seen = Assert.Single(h.Probe.Seen);
        Assert.Equal(ManagementRole.Editor.ToLevel(), seen.RoleLevel);
        await AssertResolverAskedOnceAsync(h);
    }

    [Fact]
    public async Task Badgeless_editor_gets_the_editor_level_in_an_everyone_floor_builtin()
    {
        AuthDbContext db = await EditorDbAsync();
        Harness h = Build(db, builtinFloor: 0);

        await h.Sut.HandleAsync(
            Event($"!{BuiltinKey}", broadcaster: false),
            CancellationToken.None
        );

        BuiltinCommandContext seen = Assert.Single(h.Probe.Seen);
        Assert.Equal(ManagementRole.Editor.ToLevel(), seen.RoleLevel);
        await AssertResolverAskedOnceAsync(h);
    }

    [Fact]
    public async Task Badgeless_editor_gets_the_editor_level_when_an_authored_command_falls_through_to_the_builtin()
    {
        AuthDbContext db = await EditorDbAsync();
        Harness h = Build(db, builtinFloor: 0, authoredFallthroughFloor: ModeratorFloor);

        await h.Sut.HandleAsync(
            Event($"!{BuiltinKey}", broadcaster: false),
            CancellationToken.None
        );

        BuiltinCommandContext seen = Assert.Single(h.Probe.Seen);
        Assert.Equal(ManagementRole.Editor.ToLevel(), seen.RoleLevel);
        await AssertResolverAskedOnceAsync(h);
    }

    [Fact]
    public async Task Badgeless_editor_sees_the_effective_role_in_a_template_reply_variable()
    {
        AuthDbContext db = await EditorDbAsync();
        Harness h = Build(db, builtinFloor: 0, templateReply: "role={user.role}");

        await h.Sut.HandleAsync(Event("!tmpl", broadcaster: false), CancellationToken.None);

        Dictionary<string, string> variables = Assert.Single(h.TemplateVariables);
        Assert.Equal(ChatRole.ToToken(PermissionLevel.Editor), variables["user.role"]);
        await AssertResolverAskedOnceAsync(h);
    }

    [Fact]
    public async Task Plain_viewer_keeps_level_zero_in_the_builtin()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Harness h = Build(db, builtinFloor: 0);

        await h.Sut.HandleAsync(
            Event($"!{BuiltinKey}", broadcaster: false),
            CancellationToken.None
        );

        BuiltinCommandContext seen = Assert.Single(h.Probe.Seen);
        Assert.Equal(0, seen.RoleLevel);
    }

    [Fact]
    public async Task Broadcaster_badge_gets_the_broadcaster_level_without_asking_the_resolver()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Harness h = Build(db, builtinFloor: ModeratorFloor);

        await h.Sut.HandleAsync(Event($"!{BuiltinKey}", broadcaster: true), CancellationToken.None);

        BuiltinCommandContext seen = Assert.Single(h.Probe.Seen);
        Assert.Equal(PermissionLevel.Broadcaster.ToLevelValue(), seen.RoleLevel);
        await h.Resolver.DidNotReceiveWithAnyArgs().ResolveEffectiveLevelAsync(default, default);
    }

    private static async Task AssertResolverAskedOnceAsync(Harness h)
    {
        await h
            .Resolver.Received(1)
            .ResolveEffectiveLevelAsync(ViewerUser, Broadcaster, Arg.Any<CancellationToken>());
    }

    private static async Task<AuthDbContext> EditorDbAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.ChannelMemberships.Add(
            new()
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
        return db;
    }

    private sealed record Harness(
        ChatMessageHandler Sut,
        ProbeBuiltin Probe,
        IRoleResolver Resolver,
        List<Dictionary<string, string>> TemplateVariables
    );

    /// <summary>Records every context it is executed with.</summary>
    private sealed class ProbeBuiltin(int floor) : IBuiltinCommand
    {
        public List<BuiltinCommandContext> Seen { get; } = [];

        public string BuiltinKey => BuiltinEffectiveRoleTests.BuiltinKey;
        public int DefaultCooldownSeconds => 0;
        public int DefaultMinPermissionLevel => floor;

        public Task<Result<string>> ExecuteAsync(
            BuiltinCommandContext context,
            CancellationToken ct = default
        )
        {
            Seen.Add(context);
            return Task.FromResult(Result.Success("probed"));
        }
    }

    private static Harness Build(
        AuthDbContext db,
        int builtinFloor,
        int? authoredFallthroughFloor = null,
        string? templateReply = null
    )
    {
        ChannelContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-777",
            ChannelName = "stoney_eagle",
        };
        if (authoredFallthroughFloor is { } floor)
            ctx.Commands[BuiltinKey] = new()
            {
                Name = BuiltinKey,
                TemplateResponses = [],
                GlobalCooldown = 0,
                UserCooldown = 0,
                MinPermissionLevel = floor,
                Tier = "template",
            };

        if (templateReply is not null)
            ctx.Commands["tmpl"] = new()
            {
                Name = "tmpl",
                TemplateResponses = [templateReply],
                GlobalCooldown = 0,
                UserCooldown = 0,
                MinPermissionLevel = 0,
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
        RoleResolver realResolver = new(db, clock, Substitute.For<IActAsMembershipOverlay>());
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
        services.AddSingleton(users);
        services.AddSingleton(resolver);
        ServiceProvider provider = services.BuildServiceProvider();

        ProbeBuiltin probe = new(builtinFloor);
        IBuiltinCommandCatalog catalog = Substitute.For<IBuiltinCommandCatalog>();
        catalog.Get(BuiltinKey).Returns(probe);

        IInboundOriginChatSender chat = Substitute.For<IInboundOriginChatSender>();
        chat.SendMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        chat.SendReplyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        List<Dictionary<string, string>> templateVariables = [];
        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo =>
            {
                templateVariables.Add(callInfo.ArgAt<Dictionary<string, string>>(1));
                return callInfo.ArgAt<string>(0);
            });

        ChatMessageHandler sut = new(
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ICooldownManager>(),
            chat,
            Substitute.For<IPipelineEngine>(),
            catalog,
            templates,
            Substitute.For<IEventBus>(),
            new(),
            clock,
            new OutboundSanctionAccessor(),
            TestBuiltinComposer.Create(),
            NullLogger<ChatMessageHandler>.Instance
        );

        return new(sut, probe, resolver, templateVariables);
    }

    private static ChatMessageReceivedEvent Event(string message, bool broadcaster) =>
        new()
        {
            BroadcasterId = Broadcaster,
            MessageId = "msg-7",
            TwitchBroadcasterId = "tw-777",
            UserId = TwitchUserId,
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = message,
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = broadcaster,
        };
}
