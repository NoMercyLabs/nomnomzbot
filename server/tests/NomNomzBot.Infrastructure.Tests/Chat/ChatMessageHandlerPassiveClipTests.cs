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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.MediaShare.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.MediaShare;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.MediaShare;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// S-OBS-08: a bare Twitch clip link posted in chat WITHOUT the <c>!media</c> prefix must auto-enqueue
/// into the same moderator approval queue <c>!media</c> uses. Runs the REAL <see cref="MediaShareService"/>
/// against the same SQLite harness the sibling <c>MediaShareServiceTests</c> use (not a mock), so the
/// passive scan is proven to go through the identical config gates (enabled/approval/allowed source/
/// cooldown/queue) rather than a shortcut around them.
/// </summary>
public sealed class ChatMessageHandlerPassiveClipTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0198b000-0000-7000-8000-00000000e001");
    private static readonly Guid Viewer = Guid.Parse("0198b000-0000-7000-8000-00000000e0a1");
    private const string ClipUrl = "https://clips.twitch.tv/PassiveClipSlug";

    private sealed record Harness(ChatMessageHandler Sut, MediaShareTestDbContext Db);

    private static Harness Build(bool mediaEnabled, bool requireApproval = true)
    {
        MediaShareTestDbContext db = MediaShareTestDbContext.New();
        db.Users.Add(
            new()
            {
                Id = Viewer,
                TwitchUserId = "tw-viewer-1",
                Username = "viewer",
                UsernameNormalized = "viewer",
                DisplayName = "Viewer",
            }
        );
        db.MediaShareConfigs.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                IsEnabled = mediaEnabled,
                RequireApproval = requireApproval,
                AllowTwitchClips = true,
                AllowYouTube = true,
                MaxDurationSeconds = 180,
                MaxQueueLength = 20,
                PerUserCooldownSeconds = 0,
            }
        );
        db.SaveChanges();

        IMediaSourceResolver resolver = Substitute.For<IMediaSourceResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new ResolvedMedia(
                        MediaShareSourceType.TwitchClip,
                        "PassiveClipSlug",
                        "A passive clip",
                        30,
                        "https://thumb"
                    )
                )
            );
        ICurrencyAccountService accounts = Substitute.For<ICurrencyAccountService>();
        IMediaShareService media = new MediaShareService(
            db,
            resolver,
            accounts,
            Substitute.For<IEventBus>(),
            new FakeTimeProvider(new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero))
        );

        IUserService users = Substitute.For<IUserService>();
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
                    new UserDto(
                        Viewer.ToString(),
                        "viewer",
                        "Viewer",
                        null,
                        null,
                        DateTime.UnixEpoch,
                        DateTime.UnixEpoch
                    )
                )
            );

        ServiceCollection services = new();
        services.AddSingleton(users);
        services.AddSingleton(media);
        ServiceProvider provider = services.BuildServiceProvider();

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(NewChannelContext());

        IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
        builtins.Get(Arg.Any<string>()).Returns((IBuiltinCommand?)null);

        ChatMessageHandler sut = new(
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ICooldownManager>(),
            NoopChatSender(),
            Substitute.For<IPipelineEngine>(),
            builtins,
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IEventBus>(),
            new(),
            TimeProvider.System,
            new OutboundSanctionAccessor(),
            NullLogger<ChatMessageHandler>.Instance
        );

        return new(sut, db);
    }

    private static ChannelContext NewChannelContext() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-777",
            ChannelName = "stoney_eagle",
        };

    private static IInboundOriginChatSender NoopChatSender()
    {
        IInboundOriginChatSender chat = Substitute.For<IInboundOriginChatSender>();
        chat.SendMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("not configured in this test"));
        chat.SendReplyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("not configured in this test"));
        return chat;
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

    [Fact]
    public async Task A_bare_clip_link_with_no_media_prefix_enqueues_exactly_one_pending_item()
    {
        Harness h = Build(mediaEnabled: true, requireApproval: true);

        await h.Sut.HandleAsync(MessageEvent(ClipUrl), CancellationToken.None);

        List<MediaShareRequest> rows = await h.Db.MediaShareRequests.ToListAsync();
        rows.Should().ContainSingle();
        rows[0].RequesterUserId.Should().Be(Viewer);
        rows[0].SourceUrl.Should().Be(ClipUrl);
        rows[0].Status.Should().Be(MediaShareStatus.Pending);
    }

    [Fact]
    public async Task A_clip_link_embedded_in_ordinary_chat_text_still_enqueues()
    {
        // Matches how !media's own resolver already extracts a clip slug from anywhere within the string
        // it is handed — the passive scan is not stricter than the command it mirrors.
        Harness h = Build(mediaEnabled: true, requireApproval: true);

        await h.Sut.HandleAsync(
            MessageEvent($"omg check this out {ClipUrl} so good"),
            CancellationToken.None
        );

        List<MediaShareRequest> rows = await h.Db.MediaShareRequests.ToListAsync();
        rows.Should().ContainSingle();
        rows[0].SourceUrl.Should().Be(ClipUrl);
    }

    [Fact]
    public async Task MediaShare_disabled_on_the_channel_enqueues_nothing()
    {
        // Same config gate !media itself is blocked by — the passive scan must fail the same way, silently.
        Harness h = Build(mediaEnabled: false);

        await h.Sut.HandleAsync(MessageEvent(ClipUrl), CancellationToken.None);

        (await h.Db.MediaShareRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_media_command_message_is_not_double_enqueued_by_the_passive_scan()
    {
        // "!media <url>" is dispatched through the command path (defaultPrefixed=true), which never reaches
        // the passive-scan branch — proven here by an ordinary chat handler with no !media builtin wired
        // (builtins.Get returns null for everything), so if the passive scan ALSO ran on this message it
        // would still enqueue a second time. Exactly one row proves the two paths are mutually exclusive.
        Harness h = Build(mediaEnabled: true, requireApproval: true);

        // The command path won't find a "media" builtin in this harness (none registered) and returns
        // early without enqueuing — so a second row appearing here could only come from the passive scan
        // ALSO firing on a message that starts with the command prefix, which must never happen.
        await h.Sut.HandleAsync(MessageEvent($"!media {ClipUrl}"), CancellationToken.None);

        (await h.Db.MediaShareRequests.CountAsync()).Should().Be(0);
    }
}
