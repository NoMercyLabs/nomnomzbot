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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the <c>song_request</c> pipeline action (the reward/pipeline-triggered twin of <c>!sr</c>) resolves
/// via <see cref="IMusicService.RequestTrackAsync"/> — a track link or a search query, single resolve — and
/// answers each refusal reason with its own chat wording instead of one blanket failure: not-found, a
/// blocked track (carries its typed reason), no provider connected, and a genuinely erroring provider are
/// four different messages, not one.
/// </summary>
public sealed class SongRequestActionTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ac101");

    [Fact]
    public async Task A_resolved_track_is_queued_and_a_confirmation_is_sent()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Success(
                new MusicTrack("spotify:track:abc", "Song Q", "Artist", null, null, 0, "spotify")
            )
        );

        ActionResult result = await sut.ExecuteAsync(Ctx(), Def("lofi beats"));

        result.Succeeded.Should().BeTrue();
        await chat.Received()
            .SendMessageAsync(
                ChannelId,
                Arg.Is<string>(m => m.Contains("Song Q") && m.Contains("Artist")),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>
    /// S-MUSIC-4b: the "Added to queue" confirmation is where a viewer first learns their request's code —
    /// the only handle <c>!wrongsong CODE</c> accepts. RequestTrackAsync's result carries no code (it's the
    /// search-result shape), so the action has to read it back off the just-updated queue snapshot; this
    /// proves that read-back actually lands the real code in the composed chat text, not just a placeholder.
    /// </summary>
    [Fact]
    public async Task The_confirmation_names_the_just_queued_requests_speakable_code()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Success(
                new MusicTrack("spotify:track:abc", "Song Q", "Artist", null, null, 0, "spotify")
            )
        );
        music
            .GetQueueAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new MusicQueue(
                    null,
                    [
                        new MusicQueueItem(
                            "Some Other Song",
                            "Someone Else",
                            null,
                            0,
                            "AnotherViewer",
                            Code: "N4PT"
                        ),
                        new MusicQueueItem("Song Q", "Artist", null, 0, "Bamo", Code: "K7QM"),
                    ]
                )
            );

        await sut.ExecuteAsync(Ctx(), Def("lofi beats"));

        await chat.Received()
            .SendMessageAsync(
                ChannelId,
                Arg.Is<string>(m => m.Contains("K7QM") && !m.Contains("N4PT")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Not_found_sends_a_not_found_message_and_fails_the_step()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Failure<MusicTrack>("No tracks found for \"xyz\".", "NOT_FOUND")
        );

        ActionResult result = await sut.ExecuteAsync(Ctx(), Def("xyz"));

        result.Succeeded.Should().BeFalse();
        List<string> pool =
        [
            .. ToneTemplateCatalog
                .Get(
                    PersonalityTone.Informative,
                    BuiltinResponseSlots.SongRequest.Key,
                    BuiltinResponseSlots.SongRequest.NotFound
                )
                .Select(t => "@Bamo " + t.Replace("{query}", "xyz").Replace("{user}", "Bamo")),
        ];
        string sent = (string)chat.ReceivedCalls().Single().GetArguments()[1]!;
        pool.Should().NotBeEmpty().And.Contain(sent);
    }

    [Fact]
    public async Task A_blocked_track_carries_its_typed_reason_into_chat()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Failure<MusicTrack>("\"Song Q\" is blocked in this channel.", "TRACK_BLOCKED")
        );

        await sut.ExecuteAsync(Ctx(), Def("song q"));

        await chat.Received()
            .SendMessageAsync(
                ChannelId,
                Arg.Is<string>(m => m.Contains("is blocked in this channel")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task No_provider_connected_says_requests_are_not_set_up_not_could_not_add()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Failure<MusicTrack>("No active music provider.", "SERVICE_UNAVAILABLE")
        );

        await sut.ExecuteAsync(Ctx(), Def("lofi beats"));

        await chat.Received()
            .SendMessageAsync(
                ChannelId,
                Arg.Is<string>(m => m == "@Bamo This command is currently disabled."),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_missing_operator_key_carries_its_own_reason_into_chat()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Failure<MusicTrack>(
                "YouTube song requests are not set up on this bot yet. The bot owner must add a YouTube API key.",
                "PROVIDER_NOT_CONFIGURED"
            )
        );

        await sut.ExecuteAsync(Ctx(), Def("lofi beats"));

        await chat.Received()
            .SendMessageAsync(
                ChannelId,
                Arg.Is<string>(m =>
                    m.Contains("The bot owner must add a YouTube API key")
                    && !m.Contains("Couldn't reach the music service")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>
    /// S-OWN12 sibling — the reward/pipeline-triggered song_request action had the same missing case as
    /// the !sr builtin: PER_USER_LIMIT fell through to the generic "couldn't reach the music service"
    /// wording instead of carrying MusicService's real over-limit reason into chat.
    /// </summary>
    [Fact]
    public async Task Over_the_per_user_limit_carries_its_real_reason_into_chat()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Failure<MusicTrack>(
                "You already have 2 request(s) queued — wait for one to play before adding more.",
                "PER_USER_LIMIT",
                errorData: new MusicRequestRefusal(Limit: 2)
            )
        );

        await sut.ExecuteAsync(Ctx(), Def("song q"));

        await chat.Received()
            .SendMessageAsync(
                ChannelId,
                Arg.Is<string>(m =>
                    m.Contains("You already have 2 request(s) queued")
                    && !m.Contains("Couldn't reach the music service")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_genuinely_erroring_provider_reads_differently_from_no_provider_and_not_found()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Failure<MusicTrack>("token refresh failed", "PROVIDER_ERROR")
        );

        await sut.ExecuteAsync(Ctx(), Def("lofi beats"));

        await chat.Received()
            .SendMessageAsync(
                ChannelId,
                Arg.Is<string>(m =>
                    m.Contains("try again in a moment")
                    && !m.Contains("aren't set up")
                    && !m.Contains("No tracks found")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_reward_that_picked_a_too_long_track_says_the_point_was_refunded()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Failure<MusicTrack>(
                "too long",
                "TRACK_TOO_LONG",
                errorData: new MusicRequestRefusal("Song Q", "Artist")
            )
        );
        PipelineExecutionContext ctx = Ctx(redemptionId: "redemption-1");

        await sut.ExecuteAsync(ctx, Def("song q"));

        await chat.Received()
            .SendMessageAsync(
                ChannelId,
                "@Bamo Failed to add to queue. \"Song Q\" exceeds the maximum allowed duration of 10 minutes, your point has been refunded.",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_chat_request_for_a_too_long_track_does_not_promise_a_refund()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Failure<MusicTrack>(
                "too long",
                "TRACK_TOO_LONG",
                errorData: new MusicRequestRefusal("Song Q", "Artist")
            )
        );

        await sut.ExecuteAsync(Ctx(), Def("song q"));

        await chat.Received()
            .SendMessageAsync(
                ChannelId,
                "@Bamo Failed to add to queue. \"Song Q\" exceeds the maximum allowed duration of 10 minutes.",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_refusal_speaks_in_the_channels_tone_never_in_the_services_own_sentence()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Failure<MusicTrack>(
                "RAW SERVICE SENTENCE",
                "QUEUE_FULL",
                errorData: new MusicRequestRefusal(Limit: 7)
            ),
            PersonalityTone.Sassy
        );

        await sut.ExecuteAsync(Ctx(), Def("song q"));

        List<string> pool =
        [
            .. ToneTemplateCatalog
                .Get(
                    PersonalityTone.Sassy,
                    BuiltinResponseSlots.SongRequest.Key,
                    BuiltinResponseSlots.SongRequest.QueueFull
                )
                .Select(t => "@Bamo " + t.Replace("{queue.max}", "7")),
        ];
        string sent = (string)chat.ReceivedCalls().Single().GetArguments()[1]!;
        sent.Should().NotContain("RAW SERVICE SENTENCE");
        pool.Should().Contain(sent);
    }

    [Fact]
    public async Task A_duplicate_reward_names_the_first_requester_from_the_duplicate_pool()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Failure<MusicTrack>(
                "RAW SERVICE SENTENCE",
                "DUPLICATE_TRACK",
                errorData: new MusicRequestRefusal("Song Q", "Artist", "viewer1")
            ),
            PersonalityTone.Sassy
        );

        await sut.ExecuteAsync(Ctx(), Def("song q"));

        List<string> pool =
        [
            .. ToneTemplateCatalog
                .Get(
                    PersonalityTone.Sassy,
                    BuiltinResponseSlots.SongRequest.Key,
                    BuiltinResponseSlots.SongRequest.Duplicate
                )
                .Select(t =>
                    "@Bamo "
                    + t.Replace("{track.name}", "Song Q")
                        .Replace("{requested.by}", "viewer1")
                        .Replace("{user}", "Bamo")
                ),
        ];
        string sent = (string)chat.ReceivedCalls().Single().GetArguments()[1]!;
        pool.Should().Contain(sent);
    }

    [Fact]
    public async Task The_confirmation_comes_from_the_added_with_code_slot()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Success(
                new MusicTrack("spotify:track:abc", "Song Q", "Artist", null, null, 0, "spotify")
            )
        );
        music
            .GetQueueAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new MusicQueue(
                    null,
                    [new MusicQueueItem("Song Q", "Artist", null, 0, "Bamo", Code: "N4PT")]
                )
            );

        await sut.ExecuteAsync(Ctx(), Def("lofi beats"));

        List<string> pool =
        [
            .. ToneTemplateCatalog
                .Get(
                    PersonalityTone.Informative,
                    BuiltinResponseSlots.SongRequest.Key,
                    BuiltinResponseSlots.SongRequest.AddedWithCode
                )
                .Select(t =>
                    "@Bamo "
                    + t.Replace("{track.name}", "Song Q")
                        .Replace("{track.artist}", "Artist")
                        .Replace("{request.code}", "N4PT")
                        .Replace("{user}", "Bamo")
                ),
        ];
        string sent = (string)chat.ReceivedCalls().Single().GetArguments()[1]!;
        pool.Should().NotBeEmpty().And.Contain(sent);
        sent.Should().Contain("N4PT");
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (SongRequestAction Sut, IMusicService Music, IChatProvider Chat) Build(
        Result<MusicTrack> requestResult,
        string personality = PersonalityTone.Informative
    )
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string template = call.ArgAt<string>(0);
                foreach (
                    KeyValuePair<string, string> v in call.ArgAt<IDictionary<string, string>>(1)
                )
                    template = template.Replace($"{{{v.Key}}}", v.Value);
                return Task.FromResult(template);
            });
        BuiltinResponseComposer composer = new(
            resolver,
            NoPlatformBuiltinReplies.Instance,
            FakeChannelBuiltinReplies.None
        );
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry
            .Get(ChannelId)
            .Returns(
                new ChannelContext
                {
                    BroadcasterId = ChannelId,
                    TwitchChannelId = "1",
                    ChannelName = "chan",
                    Personality = personality,
                }
            );

        IMusicService music = Substitute.For<IMusicService>();
        music
            .RequestTrackAsync(
                ChannelId.ToString(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            )
            .Returns(requestResult);
        // Default: an empty queue, so the code-lookup read-back is a no-op for every test that doesn't
        // care about the code — only "The_confirmation_names_..." below overrides this.
        music
            .GetQueueAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(new MusicQueue(null, []));

        IChatProvider chat = Substitute.For<IChatProvider>();
        SongRequestAction sut = new(
            music,
            chat,
            composer,
            registry,
            NullLogger<SongRequestAction>.Instance
        );
        return (sut, music, chat);
    }

    private static PipelineExecutionContext Ctx(string? redemptionId = null) =>
        new()
        {
            RedemptionId = redemptionId,
            BroadcasterId = ChannelId,
            TriggeredByUserId = "twitch-42",
            TriggeredByDisplayName = "Bamo",
            MessageId = "msg-1",
            RawMessage = "!sr",
        };

    private static ActionDefinition Def(string query) =>
        new()
        {
            Type = "song_request",
            Parameters = new() { ["query"] = JsonSerializer.SerializeToElement(query) },
        };
}
