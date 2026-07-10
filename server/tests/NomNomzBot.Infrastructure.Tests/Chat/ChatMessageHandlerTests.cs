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
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
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
            .SendMessageAsync(Broadcaster, BuiltinResponse, Arg.Any<CancellationToken>());
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
            .SendMessageAsync(Broadcaster, BuiltinResponse, Arg.Any<CancellationToken>());
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

    // ── shared scaffolding ──────────────────────────────────────────────────

    private static ChannelContext NewChannelContext() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-777",
            ChannelName = "stoney_eagle",
        };

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
}
