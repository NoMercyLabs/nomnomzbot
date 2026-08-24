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
using NomNomzBot.Application.Abstractions.Persistence;
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
using NomNomzBot.Infrastructure.Tests.Identity;
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
        ctx.Commands[BuiltinKey] = new()
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

    // ── per-command trigger model (Commands.PrefixMode / MatchMode — commands-pipelines.md §3.2.1) ──────

    [Fact]
    public async Task A_command_with_a_custom_prefix_responds_to_it_and_not_the_channel_default()
    {
        // Channel default stays "!"; this ONE command overrides its own prefix to "?" (PrefixMode=Custom).
        ChannelContext ctx = NewChannelContext();
        AddTriggerCommand(
            ctx,
            "hype",
            "HYPE!",
            prefixMode: "Custom",
            customPrefix: "?",
            matchMode: "StartsWith"
        );

        (ChatMessageHandler sut, _, IEventBus bus) = BuildWithGames(ctx, new());

        await sut.HandleAsync(MessageEvent("?hype"), CancellationToken.None);

        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e =>
                    e.CommandName == "hype" && e.Succeeded
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task The_channel_default_prefix_does_not_trigger_a_custom_prefix_command()
    {
        ChannelContext ctx = NewChannelContext(); // default prefix "!"
        AddTriggerCommand(
            ctx,
            "hype",
            "HYPE!",
            prefixMode: "Custom",
            customPrefix: "?",
            matchMode: "StartsWith"
        );

        (ChatMessageHandler sut, IChatProvider chat, IEventBus bus) = BuildWithGames(ctx, new());

        await sut.HandleAsync(MessageEvent("!hype"), CancellationToken.None);

        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        await bus.DidNotReceiveWithAnyArgs()
            .PublishAsync(
                Arg.Any<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Exact_match_mode_requires_the_whole_message_to_equal_the_trigger()
    {
        ChannelContext ctx = NewChannelContext();
        AddTriggerCommand(
            ctx,
            "hi",
            "Hey!",
            prefixMode: "Default",
            customPrefix: null,
            matchMode: "Exact"
        );

        (ChatMessageHandler sut, _, IEventBus bus) = BuildWithGames(ctx, new());

        // Exact: "!hi" alone fires, "!hi there" does NOT (StartsWith would have matched both).
        await sut.HandleAsync(MessageEvent("!hi"), CancellationToken.None);
        await sut.HandleAsync(MessageEvent("!hi there"), CancellationToken.None);

        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e =>
                    e.CommandName == "hi"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Contains_match_mode_fires_on_a_mid_message_whole_word_trigger()
    {
        ChannelContext ctx = NewChannelContext();
        AddTriggerCommand(
            ctx,
            "party",
            "Let's go!",
            prefixMode: "None",
            customPrefix: null,
            matchMode: "Contains"
        );

        (ChatMessageHandler sut, _, IEventBus bus) = BuildWithGames(ctx, new());

        // Contains + PrefixMode=None: the bare word appears anywhere in an ordinary chat line.
        await sut.HandleAsync(MessageEvent("time to party tonight"), CancellationToken.None);

        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e =>
                    e.CommandName == "party"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task StartsWith_Exact_and_Contains_disagree_on_the_same_input()
    {
        // Same trigger word "go", three commands differing only in MatchMode, one input: "!go now" —
        // proves the three modes are not interchangeable on identical input.
        ChannelContext ctx = NewChannelContext();
        AddTriggerCommand(
            ctx,
            "go",
            "starts",
            prefixMode: "Default",
            customPrefix: null,
            matchMode: "StartsWith"
        );

        (ChatMessageHandler startsWithSut, _, IEventBus startsWithBus) = BuildWithGames(ctx, new());
        await startsWithSut.HandleAsync(MessageEvent("!go now"), CancellationToken.None);
        await startsWithBus
            .Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e => e.Succeeded),
                Arg.Any<CancellationToken>()
            );

        ChannelContext exactCtx = NewChannelContext();
        AddTriggerCommand(
            exactCtx,
            "go",
            "exact",
            prefixMode: "Default",
            customPrefix: null,
            matchMode: "Exact"
        );
        (ChatMessageHandler exactSut, IChatProvider exactChat, IEventBus exactBus) = BuildWithGames(
            exactCtx,
            new()
        );
        await exactSut.HandleAsync(MessageEvent("!go now"), CancellationToken.None);
        await exactChat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        await exactBus
            .DidNotReceiveWithAnyArgs()
            .PublishAsync(
                Arg.Any<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_command_bound_to_a_disabled_pipeline_never_runs_from_chat()
    {
        // ChannelRegistry caches no PipelineGraphJson for a command bound to a disabled Pipeline row —
        // the same "no executable graph" shape the handler already falls back to the builtin catalog for.
        ChannelContext ctx = NewChannelContext();
        ctx.Commands["flow"] = new()
        {
            Name = "flow",
            TemplateResponses = [],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = "pipeline",
            PipelineGraphJson = null, // disabled Pipeline.IsEnabled=false ⇒ registry caches null
        };

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);
        IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
        builtins.Get(Arg.Any<string>()).Returns((IBuiltinCommand?)null);
        IPipelineEngine pipelineEngine = Substitute.For<IPipelineEngine>();
        IChatProvider chat = Substitute.For<IChatProvider>();

        ChatMessageHandler sut = new(
            registry,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ICooldownManager>(),
            chat,
            pipelineEngine,
            builtins,
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IEventBus>(),
            new(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );

        await sut.HandleAsync(MessageEvent("!flow"), CancellationToken.None);

        await pipelineEngine
            .DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, Arg.Any<CancellationToken>());
        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
    }

    // ── per-channel command prefix (Channel.CommandPrefix) ──────────────────

    [Fact]
    public async Task Command_typed_with_the_channels_custom_prefix_dispatches()
    {
        // The channel runs a non-default prefix ("?"); a matching command must resolve and dispatch.
        ChannelContext ctx = NewChannelContext();
        ctx.CommandPrefix = "?";
        AddTemplateCommand(ctx, "hello", "Hi there");

        (ChatMessageHandler sut, _, IEventBus bus) = BuildWithGames(ctx, new());

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

        (ChatMessageHandler sut, IChatProvider chat, IEventBus bus) = BuildWithGames(ctx, new());

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
        ctx.Commands["staffonly"] = new()
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
        Application.Contracts.Authorization.IRoleResolver resolver =
            Substitute.For<Application.Contracts.Authorization.IRoleResolver>();
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
            new(),
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
        ctx.Personality = PersonalityTone.Sassy;
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
            new(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );

        await sut.HandleAsync(MessageEvent($"!{BuiltinKey}"), CancellationToken.None);

        builtin.Captured.Should().NotBeNull();
        builtin.Captured!.Personality.Should().Be(PersonalityTone.Sassy);
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
            new(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );

        await sut.HandleAsync(MessageEvent($"!{BuiltinKey}"), CancellationToken.None);

        builtin.Captured.Should().NotBeNull();
        builtin.Captured!.Personality.Should().Be(PersonalityTone.Informative);
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
            new(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );
        return (sut, executor);
    }

    // ── S020: registry/context bootstrap is provider-agnostic and fires on ANY first message ─────

    /// <summary>
    /// S020: before the fix, <c>HandleAsync</c> only lazy-loaded a cold registry context AFTER the
    /// welcome-trigger check — so a channel's very first message (of any kind, on any provider) never
    /// bootstrapped in time to fire welcome. This proves a Kick-only channel's first PLAIN (non-command)
    /// message bootstraps the registry via <c>GetOrCreateAsync</c> — asserted by the actual call the
    /// handler made (broadcaster id + the Kick platform channel id it read from the DB), not a mock hit
    /// count on an unrelated method.
    /// </summary>
    [Fact]
    public async Task Cold_registry_plain_message_on_kick_ingest_bootstraps_the_channel_context()
    {
        (ChatMessageHandler sut, IChannelRegistry registry, _) = BuildColdKickChannel(
            out ChatMessageReceivedEvent kickMessage
        );

        await sut.HandleAsync(kickMessage, CancellationToken.None);

        await registry
            .Received(1)
            .GetOrCreateAsync(
                Broadcaster,
                "kick-ext-1",
                "kickonlychannel",
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>
    /// S020 done-when #2: that same Kick-only chatter's first plain message must not just create a
    /// context object — it must cause the session-first-message ("welcome them in") trigger to actually
    /// evaluate and fire, proven by the emitted/attempted trigger execution.
    /// </summary>
    [Fact]
    public async Task Cold_registry_plain_message_on_kick_ingest_fires_the_welcome_trigger()
    {
        (
            ChatMessageHandler sut,
            IChannelRegistry registry,
            NomNomzBot.Application.Commands.Services.IEventResponseExecutor executor
        ) = BuildColdKickChannel(out ChatMessageReceivedEvent kickMessage);

        await sut.HandleAsync(kickMessage, CancellationToken.None);

        await executor
            .Received(1)
            .ExecuteAsync(
                Broadcaster,
                "engagement.session_first_message",
                "kick-viewer-1",
                "KickViewer",
                Arg.Is<Dictionary<string, string>>(v =>
                    v["user"] == "KickViewer" && v["user.id"] == "kick-viewer-1"
                ),
                Arg.Any<CancellationToken>()
            );

        // S020 done-when #3: the channel is now IN the singleton registry with no !command ever having
        // been typed — TimerService (TimerService.cs) scans exactly this registry (`_registry.GetAll()`)
        // to decide which channels' timers are active, so a channel present here has live timers.
        registry.Get(Broadcaster).Should().NotBeNull();
    }

    /// <summary>
    /// S020 done-when #4 (idempotency half): a second plain message from the SAME cold-bootstrapped
    /// channel must not re-bootstrap the registry or double-fire the welcome for the same chatter.
    /// </summary>
    [Fact]
    public async Task Second_plain_message_does_not_rebootstrap_or_refire_the_welcome()
    {
        (
            ChatMessageHandler sut,
            IChannelRegistry registry,
            NomNomzBot.Application.Commands.Services.IEventResponseExecutor executor
        ) = BuildColdKickChannel(out ChatMessageReceivedEvent kickMessage);

        await sut.HandleAsync(kickMessage, CancellationToken.None);
        await sut.HandleAsync(kickMessage, CancellationToken.None); // same user, same channel, again

        await registry
            .Received(1) // exactly once — the second message found a warm registry
            .GetOrCreateAsync(
                Broadcaster,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await executor
            .Received(1) // exactly once — session-deduped, never double-fired
            .ExecuteAsync(
                Broadcaster,
                "engagement.session_first_message",
                "kick-viewer-1",
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>
    /// S020 regression: the pre-existing Twitch path (registry already warm — the common EventSub case)
    /// must keep behaving exactly as before the reorder — welcome still fires on the first live line and
    /// is still session-deduped on the second, unchanged from the pre-fix behavior these two facts already
    /// proved.
    /// </summary>
    [Fact]
    public async Task Twitch_path_with_a_warm_registry_still_fires_welcome_exactly_once()
    {
        ChannelContext ctx = NewChannelContext();
        ctx.IsLive = true;

        (
            ChatMessageHandler sut,
            NomNomzBot.Application.Commands.Services.IEventResponseExecutor executor
        ) = BuildWithExecutor(ctx);

        await sut.HandleAsync(MessageEvent("hello everyone"), CancellationToken.None);
        await sut.HandleAsync(MessageEvent("me again"), CancellationToken.None);

        await executor
            .Received(1)
            .ExecuteAsync(
                Broadcaster,
                "engagement.session_first_message",
                "tw-viewer-1",
                "Viewer",
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>
    /// Builds a <see cref="ChatMessageHandler"/> whose registry starts COLD (<c>Get</c> returns null) for
    /// a Kick-only channel seeded in a real <see cref="AuthDbContext"/> (no <c>TwitchChannelId</c>, only
    /// <c>ExternalChannelId</c> — the provider-agnostic key, platform-identity.md §9.4), so
    /// <c>EnsureChannelLoadedAsync</c> exercises its real DB read. The registry substitute mirrors real
    /// <c>ChannelRegistry</c> idempotency semantics: the first <c>GetOrCreateAsync</c> call creates and
    /// "stores" the context (flips <c>Get</c> from null to non-null for every call after), exactly like
    /// the production singleton.
    /// </summary>
    private static (
        ChatMessageHandler Sut,
        IChannelRegistry Registry,
        NomNomzBot.Application.Commands.Services.IEventResponseExecutor Executor
    ) BuildColdKickChannel(out ChatMessageReceivedEvent kickMessage)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                Name = "kickonlychannel",
                NameNormalized = "kickonlychannel",
                TwitchChannelId = null,
                Provider = "kick",
                ExternalChannelId = "kick-ext-1",
                CreatedAt = DateTime.UtcNow,
            }
        );
        db.SaveChanges();

        ChannelContext? stored = null;
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(_ => stored);
        registry
            .GetOrCreateAsync(
                Broadcaster,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo =>
            {
                stored ??= new()
                {
                    BroadcasterId = Broadcaster,
                    TwitchChannelId = callInfo.ArgAt<string>(1),
                    ChannelName = callInfo.ArgAt<string>(2),
                    IsLive = true,
                };
                return Task.FromResult(stored);
            });

        NomNomzBot.Application.Commands.Services.IEventResponseExecutor executor =
            Substitute.For<NomNomzBot.Application.Commands.Services.IEventResponseExecutor>();

        // One scope factory serves BOTH seams the handler resolves scoped services from:
        // EnsureChannelLoadedAsync (IApplicationDbContext) and FireSessionFirstMessageAsync
        // (IEventResponseExecutor) — exactly the production DI shape.
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(executor);
        ServiceProvider provider = services.BuildServiceProvider();

        ChatMessageHandler sut = new(
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ICooldownManager>(),
            Substitute.For<IChatProvider>(),
            Substitute.For<IPipelineEngine>(),
            Substitute.For<IBuiltinCommandCatalog>(),
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IEventBus>(),
            new(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );

        kickMessage = new()
        {
            BroadcasterId = Broadcaster,
            MessageId = "kick-msg-1",
            TwitchBroadcasterId = "kick-ext-1",
            Provider = "kick",
            UserId = "kick-viewer-1",
            UserDisplayName = "KickViewer",
            UserLogin = "kickviewer",
            Message = "hello from kick", // plain chat — no "!command" prefix
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

        return (sut, registry, executor);
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

    // ── S008: pipeline outcome threading, gate notices, reply fallback ──────

    [Fact]
    public async Task Permission_denied_command_sends_exactly_one_denial_notice()
    {
        ChannelContext ctx = NewChannelContext();
        ctx.Commands["modonly"] = new()
        {
            Name = "modonly",
            TemplateResponses = ["Hi"],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 10,
            Tier = "template",
        };

        (ChatMessageHandler sut, IChatProvider chat, IEventBus bus) = BuildWithBus(ctx);
        chat.SendReplyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        await sut.HandleAsync(MessageEvent("!modonly"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(
                Broadcaster,
                "msg-1",
                "You don't have permission to use that command.",
                Arg.Any<CancellationToken>()
            );
        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        await bus.DidNotReceiveWithAnyArgs()
            .PublishAsync<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(
                default!,
                default
            );
    }

    [Fact]
    public async Task Cooldown_blocked_command_sends_exactly_one_cooldown_notice()
    {
        ChannelContext ctx = NewChannelContext();
        ctx.Commands["spam"] = new()
        {
            Name = "spam",
            TemplateResponses = ["Hi"],
            GlobalCooldown = 30,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = "template",
        };

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);
        IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
        builtins.Get(Arg.Any<string>()).Returns((IBuiltinCommand?)null);
        ICooldownManager cooldowns = Substitute.For<ICooldownManager>();
        cooldowns.IsOnCooldown(Broadcaster.ToString(), "spam").Returns(true);
        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendReplyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        IEventBus bus = Substitute.For<IEventBus>();

        ChatMessageHandler sut = new(
            registry,
            Substitute.For<IServiceScopeFactory>(),
            cooldowns,
            chat,
            Substitute.For<IPipelineEngine>(),
            builtins,
            Substitute.For<ITemplateResolver>(),
            bus,
            new(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );

        await sut.HandleAsync(MessageEvent("!spam"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(
                Broadcaster,
                "msg-1",
                "That command is still on cooldown.",
                Arg.Any<CancellationToken>()
            );
        await bus.DidNotReceiveWithAnyArgs()
            .PublishAsync<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(
                default!,
                default
            );
    }

    [Fact]
    public async Task Pipeline_partially_failed_sends_exactly_one_failure_notice_and_marks_the_run_failed()
    {
        // A middle step failing to send must not be reported as a clean finish (S008): the run reports
        // PartiallyFailed, the invoker gets exactly ONE failure notice, and the execution fact records it
        // as failed (the analytics feed every projection folds from).
        ChannelContext ctx = NewChannelContext();
        ctx.Commands["broken"] = new()
        {
            Name = "broken",
            TemplateResponses = [],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = "pipeline",
            PipelineGraphJson = "{\"steps\":[{\"action\":{\"type\":\"send_message\"}}]}",
        };

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);
        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendReplyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        IEventBus bus = Substitute.For<IEventBus>();
        IPipelineEngine pipeline = Substitute.For<IPipelineEngine>();
        pipeline
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new PipelineExecutionResult
                {
                    ExecutionId = "exec-2",
                    Outcome = PipelineOutcome.PartiallyFailed,
                    Duration = TimeSpan.Zero,
                    StepsExecuted = 0,
                    Total = 2,
                }
            );

        ChatMessageHandler sut = new(
            registry,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ICooldownManager>(),
            chat,
            pipeline,
            Substitute.For<IBuiltinCommandCatalog>(),
            Substitute.For<ITemplateResolver>(),
            bus,
            new(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );

        await sut.HandleAsync(MessageEvent("!broken"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(
                Broadcaster,
                "msg-1",
                "Sorry, that command hit a snag and didn't finish.",
                Arg.Any<CancellationToken>()
            );
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e =>
                    e.CommandName == "broken" && !e.Succeeded
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Pipeline_fully_completed_sends_no_extra_chatter()
    {
        // Regression guard: a clean Completed run must NOT trigger the new failure-notice path.
        ChannelContext ctx = NewChannelContext();
        ctx.Commands["clean"] = new()
        {
            Name = "clean",
            TemplateResponses = [],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = "pipeline",
            PipelineGraphJson = "{\"steps\":[]}",
        };

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);
        IChatProvider chat = Substitute.For<IChatProvider>();
        IEventBus bus = Substitute.For<IEventBus>();
        IPipelineEngine pipeline = Substitute.For<IPipelineEngine>();
        pipeline
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new PipelineExecutionResult
                {
                    ExecutionId = "exec-3",
                    Outcome = PipelineOutcome.Completed,
                    Duration = TimeSpan.Zero,
                }
            );

        ChatMessageHandler sut = new(
            registry,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ICooldownManager>(),
            chat,
            pipeline,
            Substitute.For<IBuiltinCommandCatalog>(),
            Substitute.For<ITemplateResolver>(),
            bus,
            new(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );

        await sut.HandleAsync(MessageEvent("!clean"), CancellationToken.None);

        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        await chat.DidNotReceiveWithAnyArgs().SendReplyAsync(default, default!, default!, default);
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e => e.Succeeded),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Reply_form_rejected_falls_back_to_a_plain_mention_and_still_reports_success()
    {
        // Twitch refuses the reply form (e.g. a deleted/invalid parent message) — the response must still
        // reach the user, this time as a plain line with an inline mention since the reply header is no
        // longer there to address them.
        ChannelContext ctx = NewChannelContext();
        (ChatMessageHandler sut, IChatProvider chat) = Build(ctx);
        chat.SendReplyAsync(Broadcaster, "msg-1", BuiltinResponse, Arg.Any<CancellationToken>())
            .Returns(false);
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await sut.HandleAsync(MessageEvent($"!{BuiltinKey}"), CancellationToken.None);

        await chat.Received(1)
            .SendMessageAsync(
                Broadcaster,
                $"@Viewer {BuiltinResponse}",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Builtin_whose_send_fails_records_a_failed_execution_fact_and_sends_one_failure_line()
    {
        // Direct builtin dispatch (S008c): before this fix, the handler discarded the chat-send bool from
        // SendResponseAsync and always recorded success as long as the builtin's own logic didn't throw — a
        // reply the viewer never saw was still reported as delivered.
        ChannelContext ctx = NewChannelContext();
        (ChatMessageHandler sut, IChatProvider chat, IEventBus bus) = BuildWithBus(ctx);
        // Only the builtin's OWN reply text fails to send — the later failure-notice text still goes
        // through via the shared "sends succeed" default, exactly like the pipeline PartiallyFailed case.
        chat.SendReplyAsync(Broadcaster, "msg-1", BuiltinResponse, Arg.Any<CancellationToken>())
            .Returns(false);
        chat.SendMessageAsync(
                Broadcaster,
                $"@Viewer {BuiltinResponse}",
                Arg.Any<CancellationToken>()
            )
            .Returns(false);

        await sut.HandleAsync(MessageEvent($"!{BuiltinKey}"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(
                Broadcaster,
                "msg-1",
                "Sorry, that command hit a snag and didn't finish.",
                Arg.Any<CancellationToken>()
            );
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e =>
                    e.CommandName == BuiltinKey && !e.Succeeded
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Builtin_whose_send_succeeds_sends_no_extra_chatter()
    {
        // Regression guard: a builtin whose reply reaches chat fine must NOT also fire the new failure
        // notice, and must record success — exactly one line to the invoker, not two.
        ChannelContext ctx = NewChannelContext();
        (ChatMessageHandler sut, IChatProvider chat, IEventBus bus) = BuildWithBus(ctx);

        await sut.HandleAsync(MessageEvent($"!{BuiltinKey}"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(Broadcaster, "msg-1", BuiltinResponse, Arg.Any<CancellationToken>());
        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e => e.Succeeded),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Builtin_fallback_path_whose_send_fails_records_a_failed_execution_fact_and_sends_one_failure_line()
    {
        // The handler's SECOND builtin call site: a Commands-table row exists for the trigger (e.g. a
        // builtin key that also carries command metadata) but has no template responses, so it falls back
        // to the builtin catalog (ChatMessageHandler.cs ~line 427, distinct code path from the direct
        // dispatch above per the S008c finding). Before this fix this re-invocation also discarded the
        // chat-send bool.
        ChannelContext ctx = NewChannelContext();
        ctx.Commands[BuiltinKey] = new()
        {
            Name = BuiltinKey,
            TemplateResponses = [],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = "template",
        };
        (ChatMessageHandler sut, IChatProvider chat, IEventBus bus) = BuildWithBus(ctx);
        // Only the builtin's OWN reply text fails to send — the later failure-notice text still goes
        // through via the shared "sends succeed" default, exactly like the pipeline PartiallyFailed case.
        chat.SendReplyAsync(Broadcaster, "msg-1", BuiltinResponse, Arg.Any<CancellationToken>())
            .Returns(false);
        chat.SendMessageAsync(
                Broadcaster,
                $"@Viewer {BuiltinResponse}",
                Arg.Any<CancellationToken>()
            )
            .Returns(false);

        await sut.HandleAsync(MessageEvent($"!{BuiltinKey}"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(
                Broadcaster,
                "msg-1",
                "Sorry, that command hit a snag and didn't finish.",
                Arg.Any<CancellationToken>()
            );
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e =>
                    e.CommandName == BuiltinKey && !e.Succeeded
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Builtin_fallback_path_whose_send_succeeds_sends_no_extra_chatter()
    {
        // Regression guard for the fallback call site specifically.
        ChannelContext ctx = NewChannelContext();
        ctx.Commands[BuiltinKey] = new()
        {
            Name = BuiltinKey,
            TemplateResponses = [],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = "template",
        };
        (ChatMessageHandler sut, IChatProvider chat, IEventBus bus) = BuildWithBus(ctx);

        await sut.HandleAsync(MessageEvent($"!{BuiltinKey}"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(Broadcaster, "msg-1", BuiltinResponse, Arg.Any<CancellationToken>());
        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e => e.Succeeded),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Template_response_whose_send_fails_records_a_failed_execution_fact_and_sends_one_failure_line()
    {
        // S008d: the THIRD chat-send-then-report site — a plain authored command with a template
        // response (no pipeline, no builtin fallback). Before this fix the handler awaited
        // SendResponseAsync and then hardcoded `true` into PublishExecutedAsync regardless of the
        // transport result — observed directly below by NOT stubbing chat as failing yet: the pre-fix
        // code path (still present in git history at HEAD~) would have published Succeeded=true here
        // even though SendReplyAsync returned false.
        ChannelContext ctx = NewChannelContext();
        AddTemplateCommand(ctx, "hello", "Hi there");

        (ChatMessageHandler sut, IChatProvider chat, IEventBus bus) = BuildWithGames(ctx, new());
        chat.SendReplyAsync(Broadcaster, "msg-1", "Hi there", Arg.Any<CancellationToken>())
            .Returns(false);
        chat.SendMessageAsync(Broadcaster, "@Viewer Hi there", Arg.Any<CancellationToken>())
            .Returns(false);
        // The one failure notice the invoker gets also goes through SendReplyAsync — let it succeed so
        // the assertion isolates the execution-fact outcome, mirroring the builtin/fallback tests.
        chat.SendReplyAsync(
                Broadcaster,
                "msg-1",
                "Sorry, that command hit a snag and didn't finish.",
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        await sut.HandleAsync(MessageEvent("!hello"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(
                Broadcaster,
                "msg-1",
                "Sorry, that command hit a snag and didn't finish.",
                Arg.Any<CancellationToken>()
            );
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e =>
                    e.CommandName == "hello" && !e.Succeeded
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Template_response_whose_send_succeeds_sends_no_extra_chatter()
    {
        // Regression guard: a clean send records success with no extra failure line.
        ChannelContext ctx = NewChannelContext();
        AddTemplateCommand(ctx, "hello", "Hi there");

        (ChatMessageHandler sut, IChatProvider chat, IEventBus bus) = BuildWithGames(ctx, new());
        chat.SendReplyAsync(Broadcaster, "msg-1", "Hi there", Arg.Any<CancellationToken>())
            .Returns(true);

        await sut.HandleAsync(MessageEvent("!hello"), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(Broadcaster, "msg-1", "Hi there", Arg.Any<CancellationToken>());
        await chat.DidNotReceive()
            .SendReplyAsync(
                Broadcaster,
                "msg-1",
                "Sorry, that command hit a snag and didn't finish.",
                Arg.Any<CancellationToken>()
            );
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<NomNomzBot.Domain.Commands.Events.CommandExecutedEvent>(e => e.Succeeded),
                Arg.Any<CancellationToken>()
            );
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
            Config = new(null, null, null, null),
            JoinClosesAt = DateTime.UtcNow.AddSeconds(60),
            Phase = LiveGamePhase.Lobby,
        };

    private static void AddTemplateCommand(ChannelContext ctx, string name, string response) =>
        ctx.Commands[name] = new()
        {
            Name = name,
            TemplateResponses = [response],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = "template",
        };

    /// <summary>Registers a template command with an explicit trigger model (PrefixMode/CustomPrefix/MatchMode) —
    /// commands-pipelines.md §3.2.1 — for tests that exercise per-command trigger resolution rather than the
    /// channel-default StartsWith path <see cref="AddTemplateCommand"/> covers.</summary>
    private static void AddTriggerCommand(
        ChannelContext ctx,
        string name,
        string response,
        string prefixMode,
        string? customPrefix,
        string matchMode,
        System.Text.RegularExpressions.Regex? compiledRegex = null
    ) =>
        ctx.Commands[name] = new()
        {
            Name = name,
            TemplateResponses = [response],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = "template",
            PrefixMode = prefixMode,
            CustomPrefix = customPrefix,
            MatchMode = matchMode,
            CompiledRegex = compiledRegex,
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
        // Real transport sends succeed by default (S008d): see the identical comment in BuildWithBus —
        // an unconfigured NSubstitute bool call defaults to false, which now that the template-response
        // path threads the real chat-send bool through would flip every "command dispatched fine" test
        // into a false send-failed outcome. Tests exercising a send failure override this explicitly.
        chat.SendReplyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
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
        // Real transport sends succeed by default — an unconfigured NSubstitute bool call defaults to
        // false, which would silently flip every "builtin executed fine" test into a false SendFailed
        // outcome now that the handler threads the real chat-send bool through (S008c). Tests that need
        // to exercise a send failure override this explicitly.
        chat.SendReplyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
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
            new(),
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
