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
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Games;
using NomNomzBot.Infrastructure.Platform.RateLimiting;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the soundboard trigger hot path: a bare, prefix-less chat word equal to a clip's trigger word resolves
/// the clip and pushes it to the overlay audio bus; the per-clip cooldown suppresses a second play inside the
/// window; a below-floor speaker is refused; and a clip that resolves as missing/disabled never plays. The
/// trigger word CLAIMS the line, so a keyword chat trigger never also fires for the same word.
/// </summary>
public sealed class SoundTriggerMatchingTests
{
    private static readonly Guid Broadcaster = Guid.Parse("019f6d00-6666-7000-8000-000000000001");
    private static readonly Guid ClipId = Guid.Parse("019f6d00-6666-7000-8000-0000000000c1");

    private static ChannelContext NewContext() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-1",
            ChannelName = "stoney_eagle",
        };

    private static CachedSoundTrigger SoundTrigger(
        string word = "airhorn",
        int cooldownSeconds = 0,
        int minLevel = 0
    ) =>
        new()
        {
            ClipId = ClipId,
            TriggerWord = word,
            CooldownSeconds = cooldownSeconds,
            MinPermissionLevel = minLevel,
        };

    private static (
        ChatMessageHandler Sut,
        ISoundClipService Clips,
        ISoundClipOverlayNotifier Overlay,
        IChatProvider Chat
    ) Build(ChannelContext ctx, bool resolveSucceeds = true)
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);

        ISoundClipService clips = Substitute.For<ISoundClipService>();
        clips
            .ResolveForPlaybackAsync(
                Broadcaster,
                ClipId.ToString(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                resolveSucceeds
                    ? Result<SoundPlaybackDto>.Success(
                        new SoundPlaybackDto(ClipId, "https://cdn/clip.mp3", 80, 1200)
                    )
                    : Result<SoundPlaybackDto>.Failure(
                        "Sound clip not found or disabled.",
                        "NOT_FOUND"
                    )
            );

        ISoundClipOverlayNotifier overlay = Substitute.For<ISoundClipOverlayNotifier>();

        // The handler resolves the (scoped) sound services from a fresh scope on the hot path.
        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => clips)
            .AddScoped(_ => overlay)
            .BuildServiceProvider();

        IChatProvider chat = Substitute.For<IChatProvider>();

        // Empty catalog + template resolver so no chat-trigger/builtin path interferes.
        IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
        builtins.Get(Arg.Any<string>()).Returns((IBuiltinCommand?)null);

        ChatMessageHandler sut = new(
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new CooldownManager(TimeProvider.System),
            chat,
            Substitute.For<IPipelineEngine>(),
            builtins,
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IEventBus>(),
            new LiveGameSessionRegistry(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );
        return (sut, clips, overlay, chat);
    }

    private static ChatMessageReceivedEvent Line(
        string message,
        bool isModerator = false,
        string userId = "tw-viewer-1"
    ) =>
        new()
        {
            BroadcasterId = Broadcaster,
            MessageId = "msg-1",
            TwitchBroadcasterId = "tw-1",
            UserId = userId,
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = message,
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = isModerator,
            IsBroadcaster = false,
        };

    [Fact]
    public async Task The_trigger_word_resolves_the_clip_and_pushes_it_to_the_overlay()
    {
        ChannelContext ctx = NewContext();
        ctx.SoundTriggers["airhorn"] = SoundTrigger();
        (ChatMessageHandler sut, _, ISoundClipOverlayNotifier overlay, _) = Build(ctx);

        // Case-insensitive whole-message match — the bare word plays the clip.
        await sut.HandleAsync(Line("AIRHORN"), CancellationToken.None);

        await overlay
            .Received(1)
            .PlaySoundAsync(
                Broadcaster,
                Arg.Is<SoundPlaybackDto>(p => p.ClipId == ClipId && p.Volume == 80),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task The_cooldown_blocks_a_second_play_inside_the_window()
    {
        ChannelContext ctx = NewContext();
        ctx.SoundTriggers["airhorn"] = SoundTrigger(cooldownSeconds: 300);
        (ChatMessageHandler sut, _, ISoundClipOverlayNotifier overlay, _) = Build(ctx);

        await sut.HandleAsync(Line("airhorn"), CancellationToken.None);
        await sut.HandleAsync(Line("airhorn"), CancellationToken.None);

        await overlay
            .Received(1)
            .PlaySoundAsync(
                Arg.Any<Guid>(),
                Arg.Any<SoundPlaybackDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_below_permission_viewer_is_refused()
    {
        ChannelContext ctx = NewContext();
        ctx.SoundTriggers["airhorn"] = SoundTrigger(minLevel: 10); // moderator floor
        (ChatMessageHandler sut, ISoundClipService clips, ISoundClipOverlayNotifier overlay, _) =
            Build(ctx);

        await sut.HandleAsync(Line("airhorn"), CancellationToken.None);

        await overlay
            .DidNotReceiveWithAnyArgs()
            .PlaySoundAsync(default, default!, Arg.Any<CancellationToken>());
        await clips
            .DidNotReceiveWithAnyArgs()
            .ResolveForPlaybackAsync(default, default!, default, Arg.Any<CancellationToken>());

        // A moderator clears the floor and the clip plays.
        await sut.HandleAsync(Line("airhorn", isModerator: true), CancellationToken.None);
        await overlay
            .Received(1)
            .PlaySoundAsync(
                Arg.Any<Guid>(),
                Arg.Any<SoundPlaybackDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_clip_that_resolves_as_missing_or_disabled_does_not_fire()
    {
        ChannelContext ctx = NewContext();
        ctx.SoundTriggers["airhorn"] = SoundTrigger();
        (ChatMessageHandler sut, _, ISoundClipOverlayNotifier overlay, _) = Build(
            ctx,
            resolveSucceeds: false
        );

        await sut.HandleAsync(Line("airhorn"), CancellationToken.None);

        await overlay
            .DidNotReceiveWithAnyArgs()
            .PlaySoundAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_command_line_never_reaches_the_sound_trigger_surface()
    {
        ChannelContext ctx = NewContext();
        ctx.SoundTriggers["airhorn"] = SoundTrigger();
        (ChatMessageHandler sut, _, ISoundClipOverlayNotifier overlay, _) = Build(ctx);

        // The command prefix short-circuits before the non-command trigger surface.
        await sut.HandleAsync(Line("!airhorn"), CancellationToken.None);

        await overlay
            .DidNotReceiveWithAnyArgs()
            .PlaySoundAsync(default, default!, Arg.Any<CancellationToken>());
    }
}
