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
using NomNomzBot.Application.Commands.Builtin;
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
/// <c>!play</c> (moderators and up) queues the clip or video link from the message being replied to, or from
/// its own argument, approved without review. Twitch puts the parent author's mention at the start of a
/// reply, so a lone mention is not a link and the replied-to message is used.
/// </summary>
public sealed class PlayBuiltinTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-8000-00000000d001");
    private static readonly Guid Moderator = Guid.Parse("0192b400-0000-7000-8000-00000000d003");

    private const string ClipLink = "https://clips.twitch.tv/CleverClipSlug";
    private const string VideoLink = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    private sealed record Harness(PlayBuiltin Sut, IMediaShareService Media);

    private static Harness Build(Result<MediaShareRequestDto>? submit = null)
    {
        IMediaShareService media = Substitute.For<IMediaShareService>();
        media
            .SubmitAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<SubmitMediaRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(submit ?? Result.Success(Request()));

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
                        Moderator.ToString(),
                        "modmike",
                        "ModMike",
                        null,
                        null,
                        DateTime.UnixEpoch,
                        DateTime.UnixEpoch
                    )
                )
            );

        return new(new(media, users, TestBuiltinComposer.Create()), media);
    }

    private static MediaShareRequestDto Request() =>
        new(
            Guid.CreateVersion7(),
            Moderator,
            "ModMike",
            null,
            "twitch_clip",
            ClipLink,
            "CleverClipSlug",
            "Insane 1v4 clutch",
            30,
            null,
            MediaShareStatus.Approved,
            1,
            DateTime.UnixEpoch
        );

    private static BuiltinCommandContext Context(string args, string? parentBody = null) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = "tw-mod",
            TriggeringUserLogin = "modmike",
            TriggeringUserDisplayName = "ModMike",
            RoleLevel = 10,
            Args = args,
            ReplyParentMessageBody = parentBody,
        };

    private static SubmitMediaRequest Submitted(Harness h) =>
        h
            .Media.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IMediaShareService.SubmitAsync))
            .GetArguments()
            .OfType<SubmitMediaRequest>()
            .Single();

    [Fact]
    public async Task Replying_to_a_message_with_a_clip_queues_that_clip_approved_for_the_moderator()
    {
        Harness h = Build();

        Result<string> reply = await h.Sut.ExecuteAsync(
            Context("", $"look at this {ClipLink} so good")
        );

        SubmitMediaRequest sent = Submitted(h);
        sent.Url.Should().Be(ClipLink);
        sent.ApproveWithoutReview.Should().BeTrue();
        await h
            .Media.Received(1)
            .SubmitAsync(
                Channel,
                Moderator,
                Arg.Any<SubmitMediaRequest>(),
                Arg.Any<CancellationToken>()
            );
        reply.Value.Should().Be("Added Insane 1v4 clutch to the queue!");
    }

    [Fact]
    public async Task Replying_to_a_message_with_a_youtube_link_queues_the_video()
    {
        Harness h = Build();

        await h.Sut.ExecuteAsync(Context("", $"watch {VideoLink}"));

        Submitted(h).Url.Should().Be(VideoLink);
    }

    [Fact]
    public async Task The_leading_mention_twitch_adds_to_a_reply_is_not_taken_for_a_link()
    {
        Harness h = Build();

        Result<string> reply = await h.Sut.ExecuteAsync(Context("@Stoney_Eagle", ClipLink));

        Submitted(h).Url.Should().Be(ClipLink);
        reply.Value.Should().Be("Added Insane 1v4 clutch to the queue!");
    }

    [Fact]
    public async Task A_link_in_the_argument_works_after_the_leading_mention()
    {
        Harness h = Build();

        await h.Sut.ExecuteAsync(Context($"@Stoney_Eagle {VideoLink}", ClipLink));

        Submitted(h).Url.Should().Be(VideoLink);
    }

    [Fact]
    public async Task A_link_argument_without_a_reply_queues_it()
    {
        Harness h = Build();

        await h.Sut.ExecuteAsync(Context(ClipLink));

        Submitted(h).Url.Should().Be(ClipLink);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("@Stoney_Eagle", "just some words, no link")]
    [InlineData("", "https://example.com/not-a-clip")]
    public async Task No_clip_or_video_link_speaks_the_usage_reply_and_queues_nothing(
        string args,
        string? parentBody
    )
    {
        Harness h = Build();

        Result<string> reply = await h.Sut.ExecuteAsync(Context(args, parentBody));

        reply.Value.Should().Contain("!play");
        await h.Media.DidNotReceiveWithAnyArgs().SubmitAsync(default, default, default!, default);
    }

    [Fact]
    public async Task A_service_error_code_picks_its_reply_slot()
    {
        Harness h = Build(Result.Failure<MediaShareRequestDto>("service text", "QUEUE_FULL"));

        Result<string> reply = await h.Sut.ExecuteAsync(Context(ClipLink));

        reply.Value.Should().Be("The media queue is full — try again after some items play.");
    }

    [Fact]
    public void The_builtin_is_for_moderators_and_up()
    {
        Harness h = Build();

        h.Sut.BuiltinKey.Should().Be("play");
        h.Sut.DefaultMinPermissionLevel.Should().Be(10);
    }
}
