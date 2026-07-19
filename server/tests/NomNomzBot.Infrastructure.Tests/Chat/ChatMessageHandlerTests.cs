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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Games;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Games;
using NomNomzBot.Infrastructure.Games.Catalog;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the <c>ChannelBuiltinCommand.IsEnabled</c> wiring (Slice D2): <see cref="ChatMessageHandler"/>
/// consults the channel's cached builtin-toggle set (<see cref="ChannelContext.DisabledBuiltins"/>, populated
/// by <c>ChannelRegistry</c> from <c>ChannelBuiltinCommand</c>) before invoking a builtin resolved through
/// <see cref="IBuiltinCommandCatalog"/> fall-through, at both of the handler's fall-through sites. A
/// channel-disabled builtin is silently ignored — exactly like an unknown command — an enabled one still
/// executes, and a channel with no toggle row at all (the common case) defaults to enabled.
/// </summary>
public sealed class ChatMessageHandlerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0198a000-0000-7000-8000-00000000d001");

    private const string BuiltinKey = "uptime";
    private const string BuiltinResponse = "Live for 1h 23m!";

    [Fact]
    public async Task Disabled_builtin_is_silently_ignored_and_sends_no_message()
    {
        ChannelContext ctx = NewChannelContext();
        ctx.DisabledBuiltins[BuiltinKey] = 0;

        (ChatMessageHandler sut, IChatProvider chat) = Build(ctx);

        await sut.HandleAsync(MessageEvent($"!{BuiltinKey}"), CancellationToken.None);

        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        await chat.DidNotReceiveWithAnyArgs().SendReplyAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Enabled_builtin_executes_and_sends_its_response()
    {
        ChannelContext ctx = NewChannelContext();
        // Explicitly enabled looks identical to "no row at all" in the cache — ChannelRegistry only ever
        // populates DisabledBuiltins for rows where IsEnabled == false — so this proves the enabled path.

        (ChatMessageHandler sut, IChatProvider chat) = Build(ctx);

        await sut.HandleAsync(MessageEvent($"!{BuiltinKey}"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(Broadcaster, "msg-1", BuiltinResponse, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_toggle_row_defaults_to_enabled_same_as_an_explicit_enabled_row()
    {
        // A freshly loaded channel with zero ChannelBuiltinCommand rows for this key: ChannelRegistry's
        // builtin-toggle load only ever records explicitly-disabled keys, so an absent row and an explicit
        // IsEnabled=true row are indistinguishable here — both leave the cache empty for this key.
        ChannelContext ctx = NewChannelContext();
        ctx.DisabledBuiltins.Should().BeEmpty();

        (ChatMessageHandler sut, IChatProvider chat) = Build(ctx);

        await sut.HandleAsync(MessageEvent($"!{BuiltinKey}"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(Broadcaster, "msg-1", BuiltinResponse, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disabled_builtin_is_ignored_even_when_a_custom_command_row_has_no_template_responses()
    {
        // The handler's SECOND builtin fall-through site: a Commands-table row exists for the trigger (e.g. a
        // builtin key also carrying command metadata) but has no template responses, so it falls back to the
        // builtin catalog — the per-channel toggle must be honored there too, not just on the "unknown
        // command" path.
        ChannelContext ctx = NewChannelContext();
        ctx.Commands[BuiltinKey] = new CachedCommand
        {
            Name = BuiltinKey,
            TemplateResponses = [],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = "template",
        };
        ctx.DisabledBuiltins[BuiltinKey] = 0;

        (ChatMessageHandler sut, IChatProvider chat) = Build(ctx);

        await sut.HandleAsync(MessageEvent($"!{BuiltinKey}"), CancellationToken.None);

        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        await chat.DidNotReceiveWithAnyArgs().SendReplyAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Executed_builtin_publishes_the_command_executed_fact()
    {
        // The single execution fact the hub broadcast, use-count, and analytics projections fold from —
        // without it the dashboard's CommandsRun/UseCount/live push all silently stay at zero.
        ChannelContext ctx = NewChannelContext();

        (ChatMessageHandler sut, _, IEventBus bus) = BuildWithBus(ctx);

        await sut.HandleAsync(MessageEvent($"!{BuiltinKey}"), CancellationToken.None);

        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e =>
                    e.BroadcasterId == Broadcaster
                    && e.CommandName == BuiltinKey
                    && e.UserId == "tw-viewer-1"
                    && e.Username == "viewer"
                    && e.UserDisplayName == "Viewer"
                    && e.Succeeded
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Ignored_disabled_builtin_publishes_no_execution_fact()
    {
        // A silently-ignored command is NOT an execution — publishing one would fabricate analytics counts.
        ChannelContext ctx = NewChannelContext();
        ctx.DisabledBuiltins[BuiltinKey] = 0;

        (ChatMessageHandler sut, _, IEventBus bus) = BuildWithBus(ctx);

        await sut.HandleAsync(MessageEvent($"!{BuiltinKey}"), CancellationToken.None);

        await bus.DidNotReceiveWithAnyArgs()
            .PublishAsync<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(
                default!,
                default
            );
    }

    // ── per-channel command prefix (Channel.CommandPrefix) ──────────────────

    [Fact]
    public async Task Command_typed_with_the_channels_custom_prefix_dispatches()
    {
        // The channel runs a non-default prefix ("?"); a matching command must resolve and dispatch.
        ChannelContext ctx = NewChannelContext();
        ctx.CommandPrefix = "?";
        AddTemplateCommand(ctx, "hello", "Hi there");

        (ChatMessageHandler sut, _, IEventBus bus) = BuildWithGames(
            ctx,
            new LiveGameSessionRegistry()
        );

        await sut.HandleAsync(MessageEvent("?hello"), CancellationToken.None);

        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e =>
                    e.BroadcasterId == Broadcaster && e.CommandName == "hello" && e.Succeeded
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Bang_prefix_is_ignored_when_the_channels_prefix_is_custom()
    {
        // The same command typed with the DEFAULT "!" prefix must NOT dispatch once the channel's prefix is "?"
        // — otherwise the setting is cosmetic. It falls through to the ordinary-chat path (no send, no fact).
        ChannelContext ctx = NewChannelContext();
        ctx.CommandPrefix = "?";
        AddTemplateCommand(ctx, "hello", "Hi there");

        (ChatMessageHandler sut, IChatProvider chat, IEventBus bus) = BuildWithGames(
            ctx,
            new LiveGameSessionRegistry()
        );

        await sut.HandleAsync(MessageEvent("!hello"), CancellationToken.None);

        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        await chat.DidNotReceiveWithAnyArgs().SendReplyAsync(default, default!, default!, default);
        await bus.DidNotReceiveWithAnyArgs()
            .PublishAsync<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(
                default!,
                default
            );
    }

    [Fact]
    public async Task A_youtube_message_executes_commands_and_replies_through_the_platform_router()
    {
        // Since the slice-3 seam, IChatProvider IS the platform router — a YouTube chatter's command
        // executes exactly like a Twitch one, the reply routes to the YouTube send path, and the
        // execution fact publishes so analytics/use-counts fold for the YouTube tenant too.
        ChannelContext ctx = NewChannelContext();

        (ChatMessageHandler sut, IChatProvider chat, IEventBus bus) = BuildWithBus(ctx);

        ChatMessageReceivedEvent youtube = new()
        {
            BroadcasterId = Broadcaster,
            Provider = AuthEnums.Platform.YouTube,
            MessageId = "yt-msg-1",
            TwitchBroadcasterId = "UCstreamer",
            UserId = "UCviewer",
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = $"!{BuiltinKey}",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

        await sut.HandleAsync(youtube, CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(Broadcaster, "yt-msg-1", BuiltinResponse, Arg.Any<CancellationToken>());
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e =>
                    e.BroadcasterId == Broadcaster && e.CommandName == BuiltinKey && e.Succeeded
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_permit_elevated_viewer_pipeline_sees_the_effective_role_not_the_badge()
    {
        // Item 24c: pipeline user_role conditions read the SYNC `user.role` variable, so it must carry
        // the effective role — a badge-less viewer holding an Editor grant would otherwise fail
        // conditions the command gate itself honors.
        ChannelContext ctx = NewChannelContext();
        ctx.Commands["staffonly"] = new CachedCommand
        {
            Name = "staffonly",
            TemplateResponses = [],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = "pipeline",
            PipelineGraphJson = "{\"steps\":[]}",
        };

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);

        // A scope factory that really resolves the elevation seam: the badge-less viewer maps to a User
        // whose effective level resolves to Editor (30 on the unified ladder).
        Guid viewerUser = Guid.CreateVersion7();
        NomNomzBot.Application.Identity.Services.IUserService users =
            Substitute.For<NomNomzBot.Application.Identity.Services.IUserService>();
        users
            .GetOrCreateAsync(
                "tw-viewer-1",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new NomNomzBot.Application.Identity.Dtos.UserDto(
                        viewerUser.ToString(),
                        "viewer",
                        "Viewer",
                        null,
                        null,
                        DateTime.UnixEpoch,
                        DateTime.UnixEpoch
                    )
                )
            );
        NomNomzBot.Application.Contracts.Authorization.IRoleResolver resolver =
            Substitute.For<NomNomzBot.Application.Contracts.Authorization.IRoleResolver>();
        resolver
            .ResolveEffectiveLevelAsync(viewerUser, Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success(30)); // Editor

        ServiceCollection services = new();
        services.AddSingleton(users);
        services.AddSingleton(resolver);
        ServiceProvider provider = services.BuildServiceProvider();

        IPipelineEngine pipeline = Substitute.For<IPipelineEngine>();
        PipelineRequest? captured = null;
        pipeline
            .ExecuteAsync(Arg.Do<PipelineRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(
                new PipelineExecutionResult
                {
                    ExecutionId = "exec-1",
                    Outcome = PipelineOutcome.Completed,
                    Duration = TimeSpan.Zero,
                }
            );

        ChatMessageHandler sut = new(
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ICooldownManager>(),
            Substitute.For<IChatProvider>(),
            pipeline,
            Substitute.For<IBuiltinCommandCatalog>(),
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IEventBus>(),
            new LiveGameSessionRegistry(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );

        await sut.HandleAsync(MessageEvent("!staffonly"), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!
            .InitialVariables["user.role"]
            .Should()
            .Be("editor", "the pipeline variable must carry the RESOLVED effective role");
    }

    [Fact]
    public async Task Builtin_context_carries_the_channel_personality_and_override_template()
    {
        // The handler resolves the channel's tone + the per-command OverridesJson template (both cached on
        // ChannelContext) and hands them to the builtin — the seam the whole tone system hangs off.
        ChannelContext ctx = NewChannelContext();
        ctx.Personality = NomNomzBot.Domain.Identity.Enums.PersonalityTone.Sassy;
        ctx.BuiltinResponseOverrides[BuiltinKey] = "OVERRIDE {uptime}";

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);

        CapturingBuiltin builtin = new();
        IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
        builtins.Get(BuiltinKey).Returns(builtin);

        ChatMessageHandler sut = new(
            registry,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ICooldownManager>(),
            Substitute.For<IChatProvider>(),
            Substitute.For<IPipelineEngine>(),
            builtins,
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IEventBus>(),
            new LiveGameSessionRegistry(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );

        await sut.HandleAsync(MessageEvent($"!{BuiltinKey}"), CancellationToken.None);

        builtin.Captured.Should().NotBeNull();
        builtin
            .Captured!.Personality.Should()
            .Be(NomNomzBot.Domain.Identity.Enums.PersonalityTone.Sassy);
        builtin.Captured!.CustomResponseTemplate.Should().Be("OVERRIDE {uptime}");
    }

    [Fact]
    public async Task Builtin_context_defaults_personality_to_informative_with_no_override()
    {
        // A channel with no personality set and no override row: the default tone flows, override stays null.
        ChannelContext ctx = NewChannelContext();

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);

        CapturingBuiltin builtin = new();
        IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
        builtins.Get(BuiltinKey).Returns(builtin);

        ChatMessageHandler sut = new(
            registry,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ICooldownManager>(),
            Substitute.For<IChatProvider>(),
            Substitute.For<IPipelineEngine>(),
            builtins,
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IEventBus>(),
            new LiveGameSessionRegistry(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );

        await sut.HandleAsync(MessageEvent($"!{BuiltinKey}"), CancellationToken.None);

        builtin.Captured.Should().NotBeNull();
        builtin
            .Captured!.Personality.Should()
            .Be(NomNomzBot.Domain.Identity.Enums.PersonalityTone.Informative);
        builtin.Captured!.CustomResponseTemplate.Should().BeNull();
    }

    // ── session-first-message trigger (the "welcome them in" chain) ─────────

    [Fact]
    public async Task First_message_of_the_session_fires_the_welcome_trigger_exactly_once_per_user()
    {
        ChannelContext ctx = NewChannelContext();
        ctx.IsLive = true;

        (
            ChatMessageHandler sut,
            NomNomzBot.Application.Commands.Services.IEventResponseExecutor executor
        ) = BuildWithExecutor(ctx);

        await sut.HandleAsync(MessageEvent("hello everyone"), CancellationToken.None);
        await sut.HandleAsync(MessageEvent("me again"), CancellationToken.None);

        // One fire for the user's FIRST line; the second line is session-deduped.
        await executor
            .Received(1)
            .ExecuteAsync(
                Broadcaster,
                "engagement.session_first_message",
                "tw-viewer-1",
                "Viewer",
                Arg.Is<Dictionary<string, string>>(v =>
                    v["user"] == "Viewer" && v["user.id"] == "tw-viewer-1"
                ),
                Arg.Any<CancellationToken>()
            );

        // A DIFFERENT user's first line fires again.
        ChatMessageReceivedEvent second = new()
        {
            BroadcasterId = Broadcaster,
            MessageId = "msg-2",
            TwitchBroadcasterId = "tw-777",
            UserId = "tw-viewer-2",
            UserDisplayName = "Other",
            UserLogin = "other",
            Message = "hi",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };
        await sut.HandleAsync(second, CancellationToken.None);
        await executor
            .Received(1)
            .ExecuteAsync(
                Broadcaster,
                "engagement.session_first_message",
                "tw-viewer-2",
                "Other",
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );

        // The plain (non-command) chatters are now really tracked — the {chatters} fix.
        ctx.SessionChatters.Keys.Should().BeEquivalentTo("tw-viewer-1", "tw-viewer-2");
    }

    [Fact]
    public async Task Offline_chat_never_fires_the_session_welcome()
    {
        ChannelContext ctx = NewChannelContext(); // IsLive = false

        (
            ChatMessageHandler sut,
            NomNomzBot.Application.Commands.Services.IEventResponseExecutor executor
        ) = BuildWithExecutor(ctx);

        await sut.HandleAsync(MessageEvent("hello?"), CancellationToken.None);

        await executor
            .DidNotReceiveWithAnyArgs()
            .ExecuteAsync(
                default,
                default!,
                default,
                default,
                default!,
                Arg.Any<CancellationToken>()
            );
    }

    private static (
        ChatMessageHandler Sut,
        NomNomzBot.Application.Commands.Services.IEventResponseExecutor Executor
    ) BuildWithExecutor(ChannelContext ctx)
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);

        NomNomzBot.Application.Commands.Services.IEventResponseExecutor executor =
            Substitute.For<NomNomzBot.Application.Commands.Services.IEventResponseExecutor>();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton(executor)
            .BuildServiceProvider();

        ChatMessageHandler sut = new(
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ICooldownManager>(),
            Substitute.For<IChatProvider>(),
            Substitute.For<IPipelineEngine>(),
            Substitute.For<IBuiltinCommandCatalog>(),
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IEventBus>(),
            new LiveGameSessionRegistry(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );
        return (sut, executor);
    }

    // ── live-game precedence: an active round shadows a same-named command ────

    private const string HeistKeyword = "!heist";
    private const string HeistCommand = "heist";

    [Fact]
    public async Task An_active_game_session_shadows_a_same_named_command_which_never_dispatches()
    {
        // THE BUG: !heist is both an authored command AND the active Heist round's input keyword. The chat
        // event fans out to ChatMessageHandler and LiveGameInputListener independently, so both would fire —
        // the operator's command AND the join. During a live round the game must win (typing !heist means
        // JOIN the heist), so the command path stands down.
        ChannelContext ctx = NewChannelContext();
        AddTemplateCommand(ctx, HeistCommand, "Command heist fired");

        LiveGameSessionRegistry games = new();
        games.TryRegister(ActiveHeistSession()).Should().BeTrue();

        (ChatMessageHandler sut, IChatProvider chat, IEventBus bus) = BuildWithGames(ctx, games);

        await sut.HandleAsync(MessageEvent(HeistKeyword), CancellationToken.None);

        // No reply, no send, and — critically — no fabricated execution fact (analytics must not count it).
        await chat.DidNotReceiveWithAnyArgs().SendReplyAsync(default, default!, default!, default);
        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        await bus.DidNotReceiveWithAnyArgs()
            .PublishAsync<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(
                default!,
                default
            );

        // The guard is a READ-ONLY deferral: it never mutated or terminated the round, so LiveGameInputListener
        // (the authoritative consumer on its own fan-out) still owns the message and its !heist keyword.
        games.TryGet(Broadcaster, out LiveGameSessionRuntime? still).Should().BeTrue();
        still!.Terminal.Should().BeFalse();
        still.Phase.Should().Be(LiveGamePhase.Lobby);
        still.Game.Manifest.InputKeywords.Should().Contain(HeistKeyword);
    }

    [Fact]
    public async Task With_no_active_game_session_the_same_named_command_dispatches_normally()
    {
        // No round running: the guard finds nothing in the registry and the !heist command runs as usual —
        // proving the guard suppresses ONLY while a live session claims the keyword, never otherwise.
        ChannelContext ctx = NewChannelContext();
        AddTemplateCommand(ctx, HeistCommand, "Command heist fired");

        LiveGameSessionRegistry games = new(); // empty — no active round

        (ChatMessageHandler sut, IChatProvider chat, IEventBus bus) = BuildWithGames(ctx, games);

        await sut.HandleAsync(MessageEvent(HeistKeyword), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(
                Broadcaster,
                "msg-1",
                "Command heist fired",
                Arg.Any<CancellationToken>()
            );
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e =>
                    e.CommandName == HeistCommand && e.Succeeded
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_active_game_session_does_not_shadow_a_command_it_does_not_claim()
    {
        // A Heist round is live (it claims !heist only). An UNRELATED authored command (!drop) must still
        // dispatch — the guard is scoped to the ACTIVE session's keywords, it does not swallow every command
        // while a game runs. This is the discriminating case: if the guard over-matched, !drop would vanish.
        ChannelContext ctx = NewChannelContext();
        AddTemplateCommand(ctx, "drop", "Command drop fired");

        LiveGameSessionRegistry games = new();
        games.TryRegister(ActiveHeistSession()).Should().BeTrue();

        (ChatMessageHandler sut, IChatProvider chat, IEventBus bus) = BuildWithGames(ctx, games);

        await sut.HandleAsync(MessageEvent("!drop"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(
                Broadcaster,
                "msg-1",
                "Command drop fired",
                Arg.Any<CancellationToken>()
            );
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e =>
                    e.CommandName == "drop" && e.Succeeded
                ),
                Arg.Any<CancellationToken>()
            );

        // The Heist round is untouched — still active and still owning !heist for its listener.
        games.TryGet(Broadcaster, out LiveGameSessionRuntime? still).Should().BeTrue();
        still!.Terminal.Should().BeFalse();
    }

    // ── shared scaffolding ──────────────────────────────────────────────────

    private static ChannelContext NewChannelContext() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-777",
            ChannelName = "stoney_eagle",
        };

    /// <summary>A live Heist round in its join lobby — the real <see cref="HeistGame"/> so its manifest keyword
    /// (<c>!heist</c>) is exactly what the guard matches against, no test-only stand-in.</summary>
    private static LiveGameSessionRuntime ActiveHeistSession() =>
        new()
        {
            SessionId = Guid.CreateVersion7(),
            BroadcasterId = Broadcaster,
            Game = new HeistGame(),
            GameConfigId = Guid.CreateVersion7(),
            Config = new GameConfigView(null, null, null, null),
            JoinClosesAt = DateTime.UtcNow.AddSeconds(60),
            Phase = LiveGamePhase.Lobby,
        };

    private static void AddTemplateCommand(ChannelContext ctx, string name, string response) =>
        ctx.Commands[name] = new CachedCommand
        {
            Name = name,
            TemplateResponses = [response],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = "template",
        };

    private static (ChatMessageHandler Sut, IChatProvider Chat, IEventBus Bus) BuildWithGames(
        ChannelContext ctx,
        LiveGameSessionRegistry games
    )
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);

        // No builtins in play here — an unconfigured catalog returns null, so a resolved command is the ONLY
        // thing that could dispatch, keeping the collision assertions unambiguous.
        IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
        builtins.Get(Arg.Any<string>()).Returns((IBuiltinCommand?)null);

        // Echo the picked template back so a dispatched command produces an assertable reply body.
        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => Task.FromResult(callInfo.ArgAt<string>(0)));

        IChatProvider chat = Substitute.For<IChatProvider>();
        IEventBus bus = Substitute.For<IEventBus>();

        ChatMessageHandler sut = new(
            registry,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ICooldownManager>(),
            chat,
            Substitute.For<IPipelineEngine>(),
            builtins,
            templates,
            bus,
            games,
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );

        return (sut, chat, bus);
    }

    private static (ChatMessageHandler Sut, IChatProvider Chat) Build(ChannelContext ctx)
    {
        (ChatMessageHandler sut, IChatProvider chat, _) = BuildWithBus(ctx);
        return (sut, chat);
    }

    private static (ChatMessageHandler Sut, IChatProvider Chat, IEventBus Bus) BuildWithBus(
        ChannelContext ctx
    )
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);

        IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
        builtins.Get(BuiltinKey).Returns(new StubBuiltinCommand());

        IChatProvider chat = Substitute.For<IChatProvider>();
        IEventBus bus = Substitute.For<IEventBus>();

        ChatMessageHandler sut = new(
            registry,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ICooldownManager>(),
            chat,
            Substitute.For<IPipelineEngine>(),
            builtins,
            Substitute.For<ITemplateResolver>(),
            bus,
            new LiveGameSessionRegistry(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );

        return (sut, chat, bus);
    }

    private static ChatMessageReceivedEvent MessageEvent(string message) =>
        new()
        {
            BroadcasterId = Broadcaster,
            MessageId = "msg-1",
            TwitchBroadcasterId = "tw-777",
            UserId = "tw-viewer-1",
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

    /// <summary>A trivial always-everyone, no-cooldown builtin whose response proves whether it ran.</summary>
    private sealed class StubBuiltinCommand : IBuiltinCommand
    {
        public string BuiltinKey => ChatMessageHandlerTests.BuiltinKey;
        public int DefaultCooldownSeconds => 0;
        public int DefaultMinPermissionLevel => 0;

        public Task<Result<string>> ExecuteAsync(
            BuiltinCommandContext context,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(BuiltinResponse));
    }

    /// <summary>Records the <see cref="BuiltinCommandContext"/> it was handed, so the wiring can be asserted.</summary>
    private sealed class CapturingBuiltin : IBuiltinCommand
    {
        public BuiltinCommandContext? Captured { get; private set; }

        public string BuiltinKey => ChatMessageHandlerTests.BuiltinKey;
        public int DefaultCooldownSeconds => 0;
        public int DefaultMinPermissionLevel => 0;

        public Task<Result<string>> ExecuteAsync(
            BuiltinCommandContext context,
            CancellationToken ct = default
        )
        {
            Captured = context;
            return Task.FromResult(Result.Success("ok"));
        }
    }
}
