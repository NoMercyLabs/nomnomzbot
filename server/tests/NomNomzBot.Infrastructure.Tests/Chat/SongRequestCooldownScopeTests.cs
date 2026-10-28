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

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Games;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// The owner's rule: a built-in has two independent cooldown windows, one shared (global) and one per chatter.
/// Each applies when above 0, and <c>!sr</c> ships with the per-chatter window only.
/// </summary>
public sealed class SongRequestCooldownScopeTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0198a000-0000-7000-8000-00000000d003");

    [Fact]
    public void The_shipped_song_request_builtin_holds_each_viewer_to_their_own_window_only()
    {
        SongRequestBuiltin sr = new(
            Substitute.For<NomNomzBot.Application.Music.Services.IMusicService>(),
            TestBuiltinComposer.Create(),
            Substitute.For<IEventBus>()
        );

        (sr.DefaultCooldownSeconds, sr.DefaultUserCooldownSeconds).Should().Be((0, 5));
    }

    [Fact]
    public async Task A_user_window_alone_checks_and_sets_the_cooldown_under_the_chatter()
    {
        ICooldownManager cooldowns = Substitute.For<ICooldownManager>();
        ChatMessageHandler sut = Build(cooldowns, new WindowBuiltin(0, 30));

        await sut.HandleAsync(Message("viewer-1"), CancellationToken.None);

        cooldowns.Received(1).IsOnCooldown(Arg.Any<string>(), "sr", false, "tw-viewer-1");
        cooldowns
            .Received(1)
            .SetCooldown(Arg.Any<string>(), "sr", TimeSpan.FromSeconds(30), "tw-viewer-1");
        cooldowns.DidNotReceive().SetCooldown(Arg.Any<string>(), "sr", Arg.Any<TimeSpan>(), null);
        cooldowns.DidNotReceive().IsOnCooldown(Arg.Any<string>(), "sr", Arg.Any<bool>(), null);
    }

    [Fact]
    public async Task A_global_window_alone_uses_the_shared_cooldown()
    {
        ICooldownManager cooldowns = Substitute.For<ICooldownManager>();
        ChatMessageHandler sut = Build(cooldowns, new WindowBuiltin(30, 0));

        await sut.HandleAsync(Message("viewer-1"), CancellationToken.None);

        cooldowns.Received(1).SetCooldown(Arg.Any<string>(), "sr", TimeSpan.FromSeconds(30), null);
        cooldowns
            .DidNotReceive()
            .SetCooldown(Arg.Any<string>(), "sr", Arg.Any<TimeSpan>(), "tw-viewer-1");
    }

    [Fact]
    public async Task Both_windows_apply_together_each_under_its_own_scope()
    {
        ICooldownManager cooldowns = Substitute.For<ICooldownManager>();
        ChatMessageHandler sut = Build(cooldowns, new WindowBuiltin(30, 10));

        await sut.HandleAsync(Message("viewer-1"), CancellationToken.None);

        cooldowns.Received(1).SetCooldown(Arg.Any<string>(), "sr", TimeSpan.FromSeconds(30), null);
        cooldowns
            .Received(1)
            .SetCooldown(Arg.Any<string>(), "sr", TimeSpan.FromSeconds(10), "tw-viewer-1");
    }

    [Fact]
    public async Task Two_zero_windows_set_no_cooldown_at_all()
    {
        ICooldownManager cooldowns = Substitute.For<ICooldownManager>();
        ChatMessageHandler sut = Build(cooldowns, new WindowBuiltin(0, 0));

        await sut.HandleAsync(Message("viewer-1"), CancellationToken.None);

        cooldowns.DidNotReceiveWithAnyArgs().SetCooldown(default!, default!, default, default);
    }

    [Fact]
    public async Task A_channel_override_of_the_user_window_replaces_the_shipped_default()
    {
        ICooldownManager cooldowns = Substitute.For<ICooldownManager>();
        ChatMessageHandler sut = Build(cooldowns, new WindowBuiltin(0, 5), userOverride: 60);

        await sut.HandleAsync(Message("viewer-1"), CancellationToken.None);

        cooldowns
            .Received(1)
            .SetCooldown(Arg.Any<string>(), "sr", TimeSpan.FromSeconds(60), "tw-viewer-1");
    }

    [Fact]
    public async Task A_channel_override_of_zero_turns_the_user_window_off()
    {
        ICooldownManager cooldowns = Substitute.For<ICooldownManager>();
        ChatMessageHandler sut = Build(cooldowns, new WindowBuiltin(0, 5), userOverride: 0);

        await sut.HandleAsync(Message("viewer-1"), CancellationToken.None);

        cooldowns.DidNotReceiveWithAnyArgs().SetCooldown(default!, default!, default, default);
    }

    private static ChatMessageHandler Build(
        ICooldownManager cooldowns,
        IBuiltinCommand builtin,
        int? userOverride = null
    )
    {
        ChannelContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-888",
            ChannelName = "stoney_eagle",
        };
        if (userOverride is int seconds)
            ctx.BuiltinUserCooldownOverrides["sr"] = seconds;

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);
        IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
        builtins.Get("sr").Returns(builtin);
        IInboundOriginChatSender chat = Substitute.For<IInboundOriginChatSender>();
        chat.SendReplyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        chat.SendMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        return new(
            registry,
            Substitute.For<IServiceScopeFactory>(),
            cooldowns,
            TestReplyDispatcher.Over(chat),
            Substitute.For<IPipelineEngine>(),
            builtins,
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IEventBus>(),
            new LiveGameSessionRegistry(),
            TimeProvider.System,
            new OutboundSanctionAccessor(),
            TestBuiltinComposer.Create(),
            NullLogger<ChatMessageHandler>.Instance
        );
    }

    private static ChatMessageReceivedEvent Message(string login) =>
        new()
        {
            BroadcasterId = Broadcaster,
            MessageId = $"msg-{Guid.NewGuid()}",
            TwitchBroadcasterId = "tw-888",
            UserId = $"tw-{login}",
            UserDisplayName = login,
            UserLogin = login,
            Message = "!sr joliene",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    private sealed class WindowBuiltin(int globalSeconds, int userSeconds) : IBuiltinCommand
    {
        public string BuiltinKey => "sr";
        public int DefaultCooldownSeconds => globalSeconds;
        public int DefaultUserCooldownSeconds => userSeconds;
        public int DefaultMinPermissionLevel => 0;

        public Task<Result<string>> ExecuteAsync(
            BuiltinCommandContext context,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success("ok"));
    }
}
