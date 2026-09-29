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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.PlatformDefaults.Dtos;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.PlatformDefaults;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.PlatformDefaults;

/// <summary>
/// Plan item A4, family 2: event-response defaults live in the database and the runtime reads them. Proves an
/// admin edit changes what a channel that follows the platform default actually sends, never touches a channel
/// with its own response, survives the seeder's next run, counts the blast radius right, refuses a stale
/// count and an enabled default with nothing to say, is read back and audited; and that a channel's own save
/// takes it off the default while a reset puts it back.
/// </summary>
public sealed class EventResponseDefaultsAdminServiceTests
{
    private const string Follow = "channel.follow";
    private static readonly Guid Follower = Guid.Parse("0199f200-0000-7000-8000-00000000b001");
    private static readonly Guid OwnChannel = Guid.Parse("0199f200-0000-7000-8000-00000000b002");
    private static readonly Guid Admin = Guid.Parse("0199f200-0000-7000-8000-00000000b003");
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        AuthDbContext Db,
        EventResponseDefaultsAdminService Sut,
        EventResponseExecutor Executor,
        IChatProvider Chat,
        ITtsDispatchService Tts
    );

    private static async Task<Harness> BuildAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.AddRange(NewChannel(Follower, "alpha"), NewChannel(OwnChannel, "bravo"));
        await db.SaveChangesAsync();
        await new PlatformEventResponseDefaultsSeeder(db).SeedAsync();
        await new EventResponseDefaultsSeeder(db).SeedAsync();

        // bravo saved its own follow response: disabled on purpose.
        EventResponse bravoFollow = await db.EventResponses.SingleAsync(r =>
            r.BroadcasterId == OwnChannel && r.EventType == Follow
        );
        bravoFollow.FollowsPlatformDefault = false;
        bravoFollow.IsEnabled = false;
        await db.SaveChangesAsync();

        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => call.ArgAt<string>(0));
        IChatProvider chat = Substitute.For<IChatProvider>();
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        EventResponseExecutor executor = new(
            db,
            Substitute.For<IPipelineEngine>(),
            templates,
            chat,
            Substitute.For<IEventResponseOverlayNotifier>(),
            tts,
            NullLogger<EventResponseExecutor>.Instance
        );
        EventResponseDefaultsAdminService sut = new(
            db,
            new TemplateHelperValidator(),
            new FakeTimeProvider(Now)
        );
        return new(db, sut, executor, chat, tts);
    }

    private static Channel NewChannel(Guid id, string name) =>
        new()
        {
            Id = id,
            OwnerUserId = id,
            Provider = AuthEnums.Platform.Twitch,
            ExternalChannelId = name + "-ext",
            Name = name,
            NameNormalized = name,
            Status = AuthEnums.ChannelStatus.Active,
        };

    private static Task FireFollowAsync(Harness h, Guid channel) =>
        h.Executor.ExecuteAsync(channel, Follow, "u1", "viewer", new() { ["user"] = "viewer" });

    [Fact]
    public async Task A_following_channel_sends_the_shipped_platform_default_and_an_own_response_wins()
    {
        Harness h = await BuildAsync();

        await FireFollowAsync(h, Follower);
        await FireFollowAsync(h, OwnChannel);

        await h
            .Chat.Received(1)
            .SendMessageAsync(
                Follower,
                "Welcome {user}! Thanks for the follow!",
                Arg.Any<CancellationToken>()
            );
        await h
            .Chat.DidNotReceive()
            .SendMessageAsync(OwnChannel, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_admin_edit_changes_what_a_following_channel_sends_and_leaves_an_own_response_alone()
    {
        Harness h = await BuildAsync();

        Result<EventResponseDefaultDto> saved = await h.Sut.SetAsync(
            Follow,
            new(true, "Thanks {user}!", ConfirmedChannelsAffected: 1),
            Admin
        );
        await FireFollowAsync(h, Follower);
        await FireFollowAsync(h, OwnChannel);

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        await h
            .Chat.Received(1)
            .SendMessageAsync(Follower, "Thanks {user}!", Arg.Any<CancellationToken>());
        await h
            .Chat.DidNotReceive()
            .SendMessageAsync(OwnChannel, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_saved_default_is_read_back_audited_and_survives_a_re_seed()
    {
        Harness h = await BuildAsync();

        EventResponseDefaultDto saved = (
            await h.Sut.SetAsync(Follow, new(false, null, ConfirmedChannelsAffected: 1), Admin)
        ).Value;
        await new PlatformEventResponseDefaultsSeeder(h.Db).SeedAsync();

        saved.IsEnabled.Should().BeFalse();
        saved.Message.Should().BeNull();
        saved.ChannelsFollowing.Should().Be(1);
        saved.ChannelsWithOwnResponse.Should().Be(1);
        saved.Variables.Should().Contain("user");
        PlatformEventResponseDefault stored = await h
            .Db.PlatformEventResponseDefaults.AsNoTracking()
            .SingleAsync(d => d.EventType == Follow);
        stored.IsEnabled.Should().BeFalse("the seeder never overwrites the admin edit");
        stored.UpdatedByUserId.Should().Be(Admin);
        IamAuditLog audit = await h.Db.IamAuditLogs.SingleAsync();
        audit.Permission.Should().Be("platform_default:event_response");
        audit.TargetResource.Should().Be(Follow);
        audit.AffectedTenantCount.Should().Be(1);
        await FireFollowAsync(h, Follower);
        await h
            .Chat.DidNotReceive()
            .SendMessageAsync(Follower, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Blast_radius_counts_only_channels_that_follow_and_a_stale_count_is_refused()
    {
        Harness h = await BuildAsync();

        PlatformDefaultBlastRadiusDto radius = (
            await h.Sut.PreviewAsync(Follow, new(true, "Hi {user}"))
        ).Value;
        Result<EventResponseDefaultDto> stale = await h.Sut.SetAsync(
            Follow,
            new(true, "Hi {user}", ConfirmedChannelsAffected: 2),
            Admin
        );

        radius.ChannelsAffected.Should().Be(1);
        radius.ChannelsKeepingOwnSetting.Should().Be(1);
        radius.SampleChannelNames.Should().Equal("alpha");
        stale.ErrorCode.Should().Be("PREVIEW_STALE");
        (
            await h
                .Db.PlatformEventResponseDefaults.AsNoTracking()
                .SingleAsync(d => d.EventType == Follow)
        )
            .Message.Should()
            .Be("Welcome {user}! Thanks for the follow!");
        PlatformDefaultBlastRadiusDto unchanged = (
            await h.Sut.PreviewAsync(Follow, new(true, "Welcome {user}! Thanks for the follow!"))
        ).Value;
        unchanged.ChannelsAffected.Should().Be(0);
    }

    [Fact]
    public async Task An_enabled_default_needs_a_message_and_only_event_response_helpers()
    {
        Harness h = await BuildAsync();

        Result<EventResponseDefaultDto> empty = await h.Sut.SetAsync(
            Follow,
            new(true, "   ", ConfirmedChannelsAffected: 1),
            Admin
        );
        Result<PlatformDefaultBlastRadiusDto> commandOnly = await h.Sut.PreviewAsync(
            Follow,
            new(true, "Hello {args.1}")
        );

        empty.ErrorCode.Should().Be("VALIDATION_FAILED");
        commandOnly.IsFailure.Should().BeTrue("{args.*} only exists for chat commands");
    }

    [Fact]
    public async Task A_channel_save_leaves_the_default_and_a_reset_follows_it_again()
    {
        Harness h = await BuildAsync();
        EventResponseService channelService = new(
            h.Db,
            new RecordingEventBus(),
            new TemplateHelperValidator()
        );

        EventResponseDto following = (
            await channelService.GetByEventTypeAsync(Follower.ToString(), Follow)
        ).Value;
        EventResponseDto own = (
            await channelService.UpsertAsync(
                Follower.ToString(),
                Follow,
                new() { Message = "My own welcome, {user}" }
            )
        ).Value;
        await channelService.ResetToDefaultAsync(Follower.ToString(), Follow);
        EventResponseDto reset = (
            await channelService.GetByEventTypeAsync(Follower.ToString(), Follow)
        ).Value;

        following.FollowsPlatformDefault.Should().BeTrue();
        following
            .IsEnabled.Should()
            .BeTrue("the page shows what the runtime does: the platform default");
        following.Message.Should().Be("Welcome {user}! Thanks for the follow!");
        own.FollowsPlatformDefault.Should().BeFalse();
        own.IsEnabled.Should()
            .BeTrue("the first own save starts from the default it was following");
        own.Message.Should().Be("My own welcome, {user}");
        reset.FollowsPlatformDefault.Should().BeTrue();
        reset.Message.Should().Be("Welcome {user}! Thanks for the follow!");
    }

    [Fact]
    public async Task A_default_saved_with_tts_is_spoken_by_following_channels_only_and_carried_into_an_own_save()
    {
        Harness h = await BuildAsync();

        EventResponseDefaultDto saved = (
            await h.Sut.SetAsync(
                Follow,
                new(true, "Thanks {user}!", ConfirmedChannelsAffected: 1, SpeakWithTts: true),
                Admin
            )
        ).Value;
        await FireFollowAsync(h, Follower);
        await FireFollowAsync(h, OwnChannel);
        EventResponseService channelService = new(
            h.Db,
            new RecordingEventBus(),
            new TemplateHelperValidator()
        );
        EventResponseDto following = (
            await channelService.GetByEventTypeAsync(Follower.ToString(), Follow)
        ).Value;
        EventResponseDto own = (
            await channelService.UpsertAsync(
                Follower.ToString(),
                Follow,
                new() { Message = "Mine, {user}" }
            )
        ).Value;

        saved.SpeakWithTts.Should().BeTrue();
        (await h.Db.IamAuditLogs.SingleAsync())
            .Justification.Should()
            .Contain("new=enabled=True;tts=True");
        await h
            .Chat.Received(1)
            .SendMessageAsync(Follower, "Thanks {user}!", Arg.Any<CancellationToken>());
        await h
            .Tts.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r =>
                    r.BroadcasterId == Follower && r.Text == "Thanks {user}!"
                ),
                Arg.Any<CancellationToken>()
            );
        await h
            .Tts.DidNotReceive()
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r => r.BroadcasterId == OwnChannel),
                Arg.Any<CancellationToken>()
            );
        following.SpeakWithTts.Should().BeTrue("the page shows the default the channel follows");
        own.SpeakWithTts.Should()
            .BeTrue("the first own save starts from the default it was following");
    }

    [Fact]
    public async Task Toggling_only_tts_on_a_default_counts_as_a_change_in_the_blast_radius()
    {
        Harness h = await BuildAsync();

        PlatformDefaultBlastRadiusDto radius = (
            await h.Sut.PreviewAsync(
                Follow,
                new(true, "Welcome {user}! Thanks for the follow!", SpeakWithTts: true)
            )
        ).Value;

        radius.ChannelsAffected.Should().Be(1);
    }
}
