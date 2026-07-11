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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Tts.Events;
using NomNomzBot.Domain.Tts.Interfaces;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the TTS dispatch self-host leg (tts.md §3.4): a disabled channel / over-cap text is rejected with no
/// synthesis, no play, and no ledger row (a reject event fires); an enabled valid request synthesizes, stores,
/// pushes to the overlay, appends a usage-ledger row, and publishes a dispatched event; a per-viewer voice wins
/// over the channel default; a synthesis failure rejects rather than playing silence. Assertions are on the
/// collaborators actually driven + the persisted ledger.
/// </summary>
public sealed class TtsDispatchServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2a00-0000-7000-8000-000000000001");
    private const string Viewer = "viewer-123";

    private sealed class Harness
    {
        public required TtsDispatchService Service { get; init; }
        public required TtsTestDbContext Db { get; init; }
        public required ITtsService Tts { get; init; }
        public required ISoundClipStore Store { get; init; }
        public required ISoundClipOverlayNotifier Overlay { get; init; }
        public required IEventBus Bus { get; init; }
    }

    private static Harness Build(
        bool enabled = true,
        int maxLength = 500,
        string defaultVoice = "default-voice"
    )
    {
        TtsTestDbContext db = TtsTestDbContext.New();

        ITtsConfigService config = Substitute.For<ITtsConfigService>();
        config
            .GetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsConfigDto(enabled, defaultVoice, maxLength, "everyone", false, false)
                )
            );

        ITtsService tts = Substitute.For<ITtsService>();
        tts.SynthesizeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
                Task.FromResult(
                    new TtsResult(new byte[] { 1, 2, 3, 4 }, 1200, ci.ArgAt<string>(1), "edge")
                )
            );
        tts.GetAvailableVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TtsVoiceInfo>>([]));

        ISoundClipStore store = Substitute.For<ISoundClipStore>();
        store
            .PutAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<System.IO.Stream>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success("storage-key"));
        store
            .GetPlaybackUrlAsync("storage-key", Arg.Any<CancellationToken>())
            .Returns(Result.Success("https://bot.local/sounds/tts.mp3"));

        ISoundClipOverlayNotifier overlay = Substitute.For<ISoundClipOverlayNotifier>();
        IEventBus bus = Substitute.For<IEventBus>();

        TtsDispatchService service = new(
            tts,
            config,
            store,
            overlay,
            db,
            bus,
            NullLogger<TtsDispatchService>.Instance
        );
        return new Harness
        {
            Service = service,
            Db = db,
            Tts = tts,
            Store = store,
            Overlay = overlay,
            Bus = bus,
        };
    }

    private static TtsSpeakRequest Speak(string text) =>
        new(
            BroadcasterId: Tenant,
            RequestedByUserId: Guid.Empty,
            RequestedByTwitchUserId: Viewer,
            RequestedByDisplayName: "Viewer",
            Text: text,
            VoiceIdOverride: null,
            BitsAmount: 0,
            CommunityStanding: "everyone",
            SourceMessageId: null,
            StreamId: null
        );

    [Fact]
    public async Task RequestSpeakAsync_DisabledChannel_RejectsWithoutSynthOrPlayOrLedger()
    {
        Harness h = Build(enabled: false);

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(Speak("hello"));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FEATURE_DISABLED");
        await h
            .Tts.DidNotReceive()
            .SynthesizeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h
            .Overlay.DidNotReceive()
            .PlaySoundAsync(
                Arg.Any<Guid>(),
                Arg.Any<SoundPlaybackDto>(),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.TtsUsageRecords.CountAsync()).Should().Be(0);
        await h
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<TtsUtteranceRejectedEvent>(e => e.Reason == "disabled"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RequestSpeakAsync_OverCap_RejectsTooLong()
    {
        Harness h = Build(maxLength: 10);

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(
            Speak("this is definitely longer than ten characters")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        await h
            .Tts.DidNotReceive()
            .SynthesizeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<TtsUtteranceRejectedEvent>(e => e.Reason == "too_long"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RequestSpeakAsync_Enabled_Synthesizes_Stores_Pushes_Records_Publishes()
    {
        Harness h = Build();

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(
            Speak("  hello world  ")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Disposition.Should().Be(TtsDispatchDisposition.Dispatched);
        result.Value.PlaybackUrl.Should().Be("https://bot.local/sounds/tts.mp3");
        result.Value.CharacterCount.Should().Be("hello world".Length); // trimmed

        // Synthesized with the channel default voice, then played on the overlay via the sound bus.
        await h
            .Tts.Received(1)
            .SynthesizeAsync("hello world", "default-voice", Arg.Any<CancellationToken>());
        await h
            .Overlay.Received(1)
            .PlaySoundAsync(
                Tenant,
                Arg.Is<SoundPlaybackDto>(p =>
                    p.PlaybackUrl == "https://bot.local/sounds/tts.mp3" && p.DurationMs == 1200
                ),
                Arg.Any<CancellationToken>()
            );

        // A truthful usage-ledger row.
        TtsUsageRecord usage = await h.Db.TtsUsageRecords.SingleAsync();
        usage.BroadcasterId.Should().Be(Tenant);
        usage.UserId.Should().Be(Viewer);
        usage.CharacterCount.Should().Be("hello world".Length);
        usage.Provider.Should().Be("edge");
        usage.VoiceId.Should().Be("default-voice");

        await h
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<TtsUtteranceDispatchedEvent>(e =>
                    e.VoiceId == "default-voice" && e.CharacterCount == "hello world".Length
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RequestSpeakAsync_PerViewerVoice_WinsOverChannelDefault()
    {
        Harness h = Build(defaultVoice: "default-voice");
        h.Db.UserTtsVoices.Add(
            new UserTtsVoice
            {
                BroadcasterId = Tenant,
                UserId = Viewer,
                VoiceId = "viewer-chosen-voice",
            }
        );
        await h.Db.SaveChangesAsync();

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(Speak("hi"));

        result.IsSuccess.Should().BeTrue();
        result.Value.VoiceId.Should().Be("viewer-chosen-voice");
        await h
            .Tts.Received(1)
            .SynthesizeAsync("hi", "viewer-chosen-voice", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestSpeakAsync_SynthesisThrows_RejectsWithoutPlayOrLedger()
    {
        Harness h = Build();
        h.Tts.SynthesizeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<TtsResult>>(_ => throw new InvalidOperationException("provider down"));

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(Speak("hello"));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SERVICE_UNAVAILABLE");
        await h
            .Overlay.DidNotReceive()
            .PlaySoundAsync(
                Arg.Any<Guid>(),
                Arg.Any<SoundPlaybackDto>(),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.TtsUsageRecords.CountAsync()).Should().Be(0);
        await h
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<TtsUtteranceRejectedEvent>(e => e.Reason == "synthesis_failed"),
                Arg.Any<CancellationToken>()
            );
    }
}
