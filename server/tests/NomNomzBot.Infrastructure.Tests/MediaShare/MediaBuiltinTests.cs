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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Domain.MediaShare.Entities;
using NomNomzBot.Infrastructure.MediaShare.Builtins;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.MediaShare;

/// <summary>
/// <c>!media</c> speaks through the reply slots of the <c>media</c> group: the service's error CODE picks the
/// slot (its message stays for logs), the shipped wording is the slot's Informative line with the clip title
/// filled in, and a channel override replaces exactly the slot it names.
/// </summary>
public sealed class MediaBuiltinTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-8000-00000000d001");
    private static readonly Guid Viewer = Guid.Parse("0192b400-0000-7000-8000-00000000d002");

    private static MediaBuiltin Build(
        Result<MediaShareRequestDto> submit,
        FakeChannelBuiltinReplies? channelReplies = null
    )
    {
        IMediaShareService media = Substitute.For<IMediaShareService>();
        media
            .SubmitAsync(
                Channel,
                Viewer,
                Arg.Any<SubmitMediaRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(submit);

        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                Arg.Any<string>(),
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

        return new(media, users, TestBuiltinComposer.Create(channelReplies));
    }

    private static MediaShareRequestDto Request(string status) =>
        new(
            Guid.CreateVersion7(),
            Viewer,
            "Viewer",
            null,
            "twitch_clip",
            "https://clips.twitch.tv/x",
            "x",
            "Insane 1v4 clutch",
            30,
            null,
            status,
            1,
            DateTime.UnixEpoch
        );

    private static BuiltinCommandContext Context(string args) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = "tw-viewer",
            TriggeringUserLogin = "viewer",
            TriggeringUserDisplayName = "Viewer",
            Args = args,
        };

    [Fact]
    public async Task A_link_that_is_approved_speaks_the_queued_slot_with_the_title()
    {
        MediaBuiltin sut = Build(Result.Success(Request(MediaShareStatus.Approved)));

        Result<string> reply = await sut.ExecuteAsync(Context("https://clips.twitch.tv/x"));

        reply.Value.Should().Be("Added Insane 1v4 clutch to the queue!");
    }

    [Fact]
    public async Task A_link_that_needs_approval_speaks_the_pending_slot_with_the_title()
    {
        MediaBuiltin sut = Build(Result.Success(Request(MediaShareStatus.Pending)));

        Result<string> reply = await sut.ExecuteAsync(Context("https://clips.twitch.tv/x"));

        reply.Value.Should().Be("Submitted Insane 1v4 clutch — a mod will review it shortly.");
    }

    [Theory]
    [InlineData("COOLDOWN", "You're submitting too fast — wait a moment before the next one.")]
    [InlineData("QUEUE_FULL", "The media queue is full — try again after some items play.")]
    [InlineData("DISABLED", "Media share is not enabled on this channel.")]
    [InlineData("DURATION_EXCEEDED", "That clip is longer than this channel allows.")]
    [InlineData("SOME_OTHER_CODE", "I couldn't submit that. Check your points and try again.")]
    public async Task A_service_error_code_picks_its_slot_not_the_service_message(
        string code,
        string expected
    )
    {
        MediaBuiltin sut = Build(
            Result.Failure<MediaShareRequestDto>("service english text", code)
        );

        Result<string> reply = await sut.ExecuteAsync(Context("https://clips.twitch.tv/x"));

        reply.Value.Should().Be(expected);
    }

    [Fact]
    public async Task No_link_speaks_the_usage_slot()
    {
        MediaBuiltin sut = Build(Result.Success(Request(MediaShareStatus.Approved)));

        Result<string> reply = await sut.ExecuteAsync(Context(""));

        reply.Value.Should().Be("Usage: !media <twitch clip or youtube url>");
    }

    [Fact]
    public async Task A_channel_override_of_the_queued_slot_replaces_only_that_reply()
    {
        FakeChannelBuiltinReplies own = new FakeChannelBuiltinReplies().Set(
            Channel,
            BuiltinResponseSlots.Media.Key,
            BuiltinResponseSlots.Media.Queued,
            "{media.title} is up next, thanks!"
        );
        MediaBuiltin approved = Build(Result.Success(Request(MediaShareStatus.Approved)), own);
        MediaBuiltin pending = Build(Result.Success(Request(MediaShareStatus.Pending)), own);

        Result<string> queued = await approved.ExecuteAsync(Context("https://clips.twitch.tv/x"));
        Result<string> waiting = await pending.ExecuteAsync(Context("https://clips.twitch.tv/x"));

        queued.Value.Should().Be("Insane 1v4 clutch is up next, thanks!");
        waiting.Value.Should().Be("Submitted Insane 1v4 clutch — a mod will review it shortly.");
    }
}
