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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Entities;
using NomNomzBot.Infrastructure.Stream.PipelineActions;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.PipelineActions;

/// <summary>
/// Proves the shoutout action's target resolution: a numeric Twitch id passes straight through, a
/// login/channel name (the form a curated auto-shoutout list holds, @ tolerated) resolves to its id via
/// Helix Get Users before the shoutout is sent, and an unknown login fails without hitting the shoutout API.
/// </summary>
public sealed class ShoutoutActionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000b201");

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "tw-1",
            TriggeredByDisplayName = "Viewer",
            MessageId = "m1",
            RawMessage = "!so target",
        };

    private static ActionDefinition Shoutout(string userId) =>
        new()
        {
            Type = "shoutout",
            Parameters = new() { ["user_id"] = JsonSerializer.SerializeToElement(userId) },
        };

    /// <summary>Minimal stand-in for the real resolver's <c>{key}</c> substitution — enough to prove the
    /// action actually threads its seeded target vars into the announcement, without pulling in the full
    /// DB-backed resolver implementation.</summary>
    private static string NaiveResolve(NSubstitute.Core.CallInfo callInfo)
    {
        string template = (string)callInfo[0];
        IDictionary<string, string> vars = (IDictionary<string, string>)callInfo[1];
        foreach (KeyValuePair<string, string> kv in vars)
            template = template.Replace("{" + kv.Key + "}", kv.Value);
        return template;
    }

    private static TwitchUser User(string id, string login) =>
        new(
            Id: id,
            Login: login,
            DisplayName: login,
            Type: "",
            BroadcasterType: "",
            Description: "",
            ProfileImageUrl: "",
            OfflineImageUrl: "",
            ViewCount: 0,
            CreatedAt: DateTimeOffset.UnixEpoch
        );

    private static (ShoutoutAction Sut, ITwitchChatApi Chat, ITwitchUsersApi Users) Build()
    {
        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        chat.SendShoutoutAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        chat.SendAnnouncementAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
                Result.Success<IReadOnlyList<TwitchUser>>([
                    User(((IReadOnlyList<string>)callInfo[0])[0], "numerictarget"),
                ])
            );

        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => Task.FromResult(NaiveResolve(callInfo)));

        ShoutoutAction sut = new(
            chat,
            users,
            Substitute.For<IChannelRegistry>(),
            AuthTestBuilder.NewContext(),
            resolver,
            Substitute.For<ITtsDispatchService>(),
            TimeProvider.System,
            NullLogger<ShoutoutAction>.Instance
        );
        return (sut, chat, users);
    }

    [Fact]
    public async Task A_numeric_id_is_shouted_out_without_a_login_lookup()
    {
        (ShoutoutAction sut, ITwitchChatApi chat, ITwitchUsersApi users) = Build();

        ActionResult result = await sut.ExecuteAsync(Ctx(), Shoutout("123456"));

        result.Succeeded.Should().BeTrue();
        await chat.Received(1).SendShoutoutAsync(Channel, "123456", Arg.Any<CancellationToken>());
        await users.DidNotReceiveWithAnyArgs().GetUsersByLoginsAsync(default!);
    }

    [Fact]
    public async Task A_login_with_leading_at_resolves_to_its_id_before_the_shoutout()
    {
        (ShoutoutAction sut, ITwitchChatApi chat, ITwitchUsersApi users) = Build();
        users
            .GetUsersByLoginsAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "coolstreamer"),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User("789", "coolstreamer")]));

        ActionResult result = await sut.ExecuteAsync(Ctx(), Shoutout("@CoolStreamer"));

        result.Succeeded.Should().BeTrue();
        await chat.Received(1).SendShoutoutAsync(Channel, "789", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_login_fails_without_calling_the_shoutout_api()
    {
        (ShoutoutAction sut, ITwitchChatApi chat, ITwitchUsersApi users) = Build();
        users
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([]));

        ActionResult result = await sut.ExecuteAsync(Ctx(), Shoutout("ghost_channel"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ghost_channel");
        await chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!);
    }

    [Fact]
    public async Task A_variable_reference_resolves_from_the_pipeline_context_then_the_login_resolves()
    {
        // The rotating auto-shoutout shape: shoutout(user_id="{timer.message}") over a curated list.
        (ShoutoutAction sut, ITwitchChatApi chat, ITwitchUsersApi users) = Build();
        users
            .GetUsersByLoginsAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "rotationtarget"),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User("456", "rotationtarget")]));

        PipelineExecutionContext ctx = Ctx();
        ctx.Variables["timer.message"] = "RotationTarget";

        ActionResult result = await sut.ExecuteAsync(ctx, Shoutout("{timer.message}"));

        result.Succeeded.Should().BeTrue();
        await chat.Received(1).SendShoutoutAsync(Channel, "456", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Old-bot parity (S-SHOUTOUT-TARGET-TEMPLATE): the announcement template is the TARGET's own — a
    /// streamer sets how THEY want to be announced, and it's honored whoever gives the shoutout — not the
    /// shouting streamer's own default template, which the target's own (when they have one) takes priority
    /// over (ShoutoutQueueService.ExecuteShoutoutAsync read the template off the target's own Channel row).
    /// </summary>
    [Fact]
    public async Task The_targets_own_ShoutoutTemplate_wins_over_the_shouting_channels_default()
    {
        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        chat.SendShoutoutAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        chat.SendAnnouncementAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User("123456", "numerictarget")]));
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => Task.FromResult(NaiveResolve(callInfo)));

        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                Name = "stoney",
                NameNormalized = "stoney",
                OwnerUserId = Guid.NewGuid(),
                ShoutoutTemplate = "This is MY default template, never used here",
            }
        );
        db.Channels.Add(
            new()
            {
                Id = Guid.NewGuid(),
                Name = "numerictarget",
                NameNormalized = "numerictarget",
                OwnerUserId = Guid.NewGuid(),
                TwitchChannelId = "123456",
                ShoutoutTemplate = "Go watch {target.name} being awesome!",
            }
        );
        await db.SaveChangesAsync();

        ShoutoutAction sut = new(
            chat,
            users,
            Substitute.For<IChannelRegistry>(),
            db,
            resolver,
            Substitute.For<ITtsDispatchService>(),
            TimeProvider.System,
            NullLogger<ShoutoutAction>.Instance
        );

        ActionResult result = await sut.ExecuteAsync(Ctx(), Shoutout("123456"));

        result.Succeeded.Should().BeTrue();
        await chat.Received(1)
            .SendAnnouncementAsync(
                Channel,
                "Go watch numerictarget being awesome!",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>
    /// The per-target rows now carry a <c>Kind</c> — the same person can have a shoutout line AND a raid line
    /// (they are both edited from the Community viewer panel). A shoutout must read the SHOUTOUT row: picking
    /// the raid row would announce "sending you over to them" while the person is still in chat.
    /// </summary>
    [Fact]
    public async Task A_shoutout_reads_the_shoutout_line_and_never_the_same_persons_raid_line()
    {
        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        chat.SendShoutoutAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        chat.SendAnnouncementAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User("123456", "numerictarget")]));
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => Task.FromResult(NaiveResolve(callInfo)));

        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                Name = "stoney",
                NameNormalized = "stoney",
                OwnerUserId = Guid.NewGuid(),
            }
        );
        // The RAID line is deliberately listed first, so an implementation that just takes the first matching
        // target row picks the wrong one and this test fails.
        db.ShoutoutOverrides.Add(
            new()
            {
                BroadcasterId = Channel,
                TargetTwitchUserId = "123456",
                TargetDisplayName = "numerictarget",
                MessageTemplate = "Everyone head over to {target.name} now!",
                Kind = ShoutoutOverrideKinds.Raid,
            }
        );
        db.ShoutoutOverrides.Add(
            new()
            {
                BroadcasterId = Channel,
                TargetTwitchUserId = "123456",
                TargetDisplayName = "numerictarget",
                MessageTemplate = "Go give {target.name} a follow!",
                Kind = ShoutoutOverrideKinds.Shoutout,
            }
        );
        await db.SaveChangesAsync();

        ShoutoutAction sut = new(
            chat,
            users,
            Substitute.For<IChannelRegistry>(),
            db,
            resolver,
            Substitute.For<ITtsDispatchService>(),
            TimeProvider.System,
            NullLogger<ShoutoutAction>.Instance
        );

        ActionResult result = await sut.ExecuteAsync(Ctx(), Shoutout("123456"));

        result.Succeeded.Should().BeTrue();
        await chat.Received(1)
            .SendAnnouncementAsync(
                Channel,
                "Go give numerictarget a follow!",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        await chat.DidNotReceive()
            .SendAnnouncementAsync(
                Channel,
                "Everyone head over to numerictarget now!",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>
    /// The broadcaster's own per-target note (old-bot parity: the legacy bot's <c>Shoutout</c> table, keyed
    /// by channel+target) wins over BOTH the target's own self-set template AND this channel's default —
    /// it is this broadcaster's own deliberate choice about how THEY introduce this specific person.
    /// </summary>
    [Fact]
    public async Task The_shouting_channels_own_per_target_override_wins_over_everything_else()
    {
        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        chat.SendShoutoutAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        chat.SendAnnouncementAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User("123456", "numerictarget")]));
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => Task.FromResult(NaiveResolve(callInfo)));

        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                Name = "stoney",
                NameNormalized = "stoney",
                OwnerUserId = Guid.NewGuid(),
                ShoutoutTemplate = "My default template, never used here",
            }
        );
        db.Channels.Add(
            new()
            {
                Id = Guid.NewGuid(),
                Name = "numerictarget",
                NameNormalized = "numerictarget",
                OwnerUserId = Guid.NewGuid(),
                TwitchChannelId = "123456",
                ShoutoutTemplate = "The target's own self-set template, never used here either",
            }
        );
        db.ShoutoutOverrides.Add(
            new()
            {
                BroadcasterId = Channel,
                TargetTwitchUserId = "123456",
                TargetDisplayName = "numerictarget",
                MessageTemplate = "My personal note for {target.name} specifically!",
            }
        );
        await db.SaveChangesAsync();

        ShoutoutAction sut = new(
            chat,
            users,
            Substitute.For<IChannelRegistry>(),
            db,
            resolver,
            Substitute.For<ITtsDispatchService>(),
            TimeProvider.System,
            NullLogger<ShoutoutAction>.Instance
        );

        ActionResult result = await sut.ExecuteAsync(Ctx(), Shoutout("123456"));

        result.Succeeded.Should().BeTrue();
        await chat.Received(1)
            .SendAnnouncementAsync(
                Channel,
                "My personal note for numerictarget specifically!",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Always_posts_a_templated_announcement_alongside_the_native_shoutout()
    {
        (ShoutoutAction sut, ITwitchChatApi chat, ITwitchUsersApi _) = Build();

        ActionResult result = await sut.ExecuteAsync(Ctx(), Shoutout("123456"));

        result.Succeeded.Should().BeTrue();
        await chat.Received(1)
            .SendAnnouncementAsync(
                Channel,
                Arg.Is<string>(text => text.Contains("numerictarget")),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>
    /// S014: the announcement's own Result was previously logged on failure but never folded into the
    /// returned ActionResult — a broadcaster whose custom announcement failed to post (e.g. a missing
    /// user:write:chat scope) saw the pipeline step report success anyway, because only the native Helix
    /// shoutout's outcome was checked. Proves the returned outcome, not merely that nothing threw.
    /// </summary>
    [Fact]
    public async Task A_failed_announcement_is_reported_as_a_failure_even_though_the_native_shoutout_succeeded()
    {
        (ShoutoutAction sut, ITwitchChatApi chat, ITwitchUsersApi _) = Build();
        chat.SendAnnouncementAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("missing scope", "TWITCH_ERROR"));

        ActionResult result = await sut.ExecuteAsync(Ctx(), Shoutout("123456"));

        // The native shoutout was still sent (best-effort, independent of the announcement)...
        await chat.Received(1).SendShoutoutAsync(Channel, "123456", Arg.Any<CancellationToken>());
        // ...but the reported outcome is truthful: nothing claims success when the announcement failed.
        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("announcement failed");
    }

    [Fact]
    public async Task Tts_true_speaks_the_announcement_but_tts_omitted_stays_silent()
    {
        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        chat.SendShoutoutAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        chat.SendAnnouncementAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User("123456", "numerictarget")]));
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => Task.FromResult(NaiveResolve(callInfo)));
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        tts.RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(TtsDispatchDisposition.Dispatched, "v", "p", 0, 0, null)
                )
            );
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                Name = "stoney",
                NameNormalized = "stoney",
                OwnerUserId = Guid.NewGuid(),
            }
        );
        await db.SaveChangesAsync();
        ShoutoutAction sut = new(
            chat,
            users,
            Substitute.For<IChannelRegistry>(),
            db,
            resolver,
            tts,
            TimeProvider.System,
            NullLogger<ShoutoutAction>.Instance
        );

        // Manual invocation (e.g. chat-triggered !so): tts:true speaks the announcement.
        await sut.ExecuteAsync(
            Ctx(),
            new()
            {
                Type = "shoutout",
                Parameters = new()
                {
                    ["user_id"] = JsonSerializer.SerializeToElement("123456"),
                    ["tts"] = JsonSerializer.SerializeToElement(true),
                },
            }
        );
        // Speaks in the SHOUTED-OUT target's own voice (old-bot parity), not the broadcaster's — a
        // regression that silently collapsed every shoutout onto one voice, losing the per-target variety
        // configured through UserTtsVoices.
        await tts.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r =>
                    r.RequestedByTwitchUserId == "123456"
                    && r.RequestedByDisplayName == "numerictarget"
                ),
                Arg.Any<CancellationToken>()
            );

        // Automated invocation (e.g. presence-detection): tts omitted stays silent.
        tts.ClearReceivedCalls();
        await sut.ExecuteAsync(Ctx(), Shoutout("123456"));
        await tts.DidNotReceiveWithAnyArgs().RequestSpeakAsync(default!);
    }

    /// <summary>
    /// A shoutout skipped by cooldown must be visible to the invoker as a distinguishable, non-generic
    /// outcome — not collapsed into the same bare "success" a real shoutout produces, and not silent (a
    /// debug-only log the chat-side caller never sees). Proves the returned Output actually names the reason,
    /// and that no shoutout/announcement Helix calls happen while the cooldown is live.
    /// </summary>
    [Fact]
    public async Task A_shoutout_skipped_by_global_cooldown_reports_a_distinguishable_visible_outcome()
    {
        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        chat.SendShoutoutAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        chat.SendAnnouncementAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User("123456", "numerictarget")]));
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        ChannelContext channelCtx = new()
        {
            BroadcasterId = Channel,
            TwitchChannelId = "tw-channel",
            ChannelName = "stoney",
        };
        channelCtx.LastGlobalShoutout = TimeProvider.System.GetUtcNow();
        registry.Get(Channel).Returns(channelCtx);

        ShoutoutAction sut = new(
            chat,
            users,
            registry,
            AuthTestBuilder.NewContext(),
            Substitute.For<ITemplateResolver>(),
            Substitute.For<ITtsDispatchService>(),
            TimeProvider.System,
            NullLogger<ShoutoutAction>.Instance
        );

        ActionResult result = await sut.ExecuteAsync(Ctx(), Shoutout("123456"));

        result.Succeeded.Should().BeTrue();
        result.Output.Should().NotBeNullOrWhiteSpace();
        result.Output.Should().Contain("cooldown");
        await chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!);
        await chat.DidNotReceiveWithAnyArgs().SendAnnouncementAsync(default, default!, default);
    }

    [Fact]
    public async Task A_template_override_wins_over_the_channels_stored_template_and_resolves_variables()
    {
        (ShoutoutAction sut, ITwitchChatApi chat, ITwitchUsersApi _) = Build();

        PipelineExecutionContext ctx = Ctx();
        ctx.Variables["line"] = "Certified banger alert: {target}!";

        ActionResult result = await sut.ExecuteAsync(
            ctx,
            new()
            {
                Type = "shoutout",
                Parameters = new()
                {
                    ["user_id"] = JsonSerializer.SerializeToElement("123456"),
                    ["template"] = JsonSerializer.SerializeToElement("{line}"),
                },
            }
        );

        result.Succeeded.Should().BeTrue();
        await chat.Received(1)
            .SendAnnouncementAsync(
                Channel,
                "Certified banger alert: numerictarget!",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }
}
