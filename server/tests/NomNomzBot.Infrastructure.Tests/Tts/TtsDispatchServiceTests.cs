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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
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
using NomNomzBot.Infrastructure.Platform.Caching;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the TTS dispatch legs (tts.md §3.4). Self-host: a disabled channel / over-cap text is rejected with no
/// synthesis, no play, and no ledger row (a reject event fires); an enabled valid request synthesizes, stores,
/// pushes to the overlay, appends a usage-ledger row, and publishes a dispatched event; a per-viewer voice wins
/// over the channel default; the opt-out profanity censor masks the spoken text before synthesis when enabled (and
/// leaves it raw when disabled); a synthesis failure rejects rather than playing silence. Client-edge: a passing
/// request pushes a TtsSpeak overlay payload (resolved voice + censored text) and synthesizes NO server audio, while
/// the gates still reject before any push. Assertions are on the collaborators actually driven + the persisted ledger.
/// </summary>
public sealed class TtsDispatchServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2a00-0000-7000-8000-000000000001");
    private const string Viewer = "viewer-123";
    private static readonly DateTime T0 = new(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);

    private sealed class Harness
    {
        public required TtsDispatchService Service { get; init; }
        public required TtsTestDbContext Db { get; init; }
        public required ITtsService Tts { get; init; }
        public required IByokTtsProviderFactory ByokProviders { get; init; }
        public required ISoundClipStore Store { get; init; }
        public required ISoundClipOverlayNotifier Overlay { get; init; }
        public required ITtsOverlayNotifier TtsOverlay { get; init; }
        public required IEventBus Bus { get; init; }
        public required ITtsLexiconService Lexicon { get; init; }
    }

    private static Harness Build(
        bool enabled = true,
        int maxLength = 500,
        string defaultVoice = "default-voice",
        bool censorEnabled = false,
        bool modApprovalRequired = false,
        int? minBitsToTts = null,
        string mode = "self_host",
        string defaultProvider = "edge",
        Application.Contracts.Billing.IBillingTierService? tiers = null
    )
    {
        TtsTestDbContext db = TtsTestDbContext.New();

        ITtsConfigService config = Substitute.For<ITtsConfigService>();
        config
            .GetConfigAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsConfigDto(
                        enabled,
                        mode,
                        defaultProvider,
                        defaultVoice,
                        maxLength,
                        "everyone",
                        false,
                        false,
                        censorEnabled,
                        modApprovalRequired,
                        minBitsToTts
                    )
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
        ITtsOverlayNotifier ttsOverlay = Substitute.For<ITtsOverlayNotifier>();
        IEventBus bus = Substitute.For<IEventBus>();
        IByokTtsProviderFactory byokProviders = Substitute.For<IByokTtsProviderFactory>();
        // The REAL lexicon service over the same context — dispatch tests prove the substitution and its
        // write-invalidated cache end to end, not a mock echoing input.
        TtsLexiconService lexicon = new(
            db,
            new MemoryCacheService(
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<MemoryCacheService>.Instance
            )
        );

        TtsDispatchService service = new(
            tts,
            byokProviders,
            config,
            lexicon,
            new TtsProfanityCensor(),
            store,
            overlay,
            ttsOverlay,
            db,
            bus,
            tiers ?? Billing.TestTiers.Unlimited(),
            new FakeTimeProvider(new DateTimeOffset(T0)),
            NullLogger<TtsDispatchService>.Instance
        );
        return new Harness
        {
            Service = service,
            Db = db,
            Tts = tts,
            ByokProviders = byokProviders,
            Store = store,
            Overlay = overlay,
            TtsOverlay = ttsOverlay,
            Bus = bus,
            Lexicon = lexicon,
        };
    }

    private static TtsSpeakRequest Speak(string text, Guid? streamId = null) =>
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
            StreamId: streamId
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
    public async Task RequestSpeakAsync_TierCharacterCap_ClampsBelowTheStreamerSetting()
    {
        // The plan's tts_max_characters (10) is STRICTER than the streamer's own MaxLength (500) —
        // the tier cap governs (monetization-billing §3.3), so this 40+ char message is rejected.
        Harness h = Build(
            maxLength: 500,
            tiers: Billing.TestTiers.WithLimit("tts_max_characters", 10)
        );

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
    public async Task RequestSpeakAsync_BitsGate_RejectsBelowAndSpeaksAtOrAbove()
    {
        Harness h = Build(minBitsToTts: 100);

        // 40 bits < the 100-bit gate: rejected, nothing synthesized.
        Result<TtsDispatchOutcome> below = await h.Service.RequestSpeakAsync(
            Speak("hello") with
            {
                BitsAmount = 40,
            }
        );
        below.IsFailure.Should().BeTrue();
        below.ErrorCode.Should().Be("VALIDATION_FAILED");
        await h
            .Tts.DidNotReceive()
            .SynthesizeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<TtsUtteranceRejectedEvent>(e => e.Reason == "bits_gate"),
                Arg.Any<CancellationToken>()
            );

        // Exactly at the gate: speaks.
        Result<TtsDispatchOutcome> at = await h.Service.RequestSpeakAsync(
            Speak("hello") with
            {
                BitsAmount = 100,
            }
        );
        at.IsSuccess.Should().BeTrue();
        at.Value.Disposition.Should().Be(TtsDispatchDisposition.Dispatched);
    }

    [Fact]
    public async Task RequestSpeakAsync_Enabled_Synthesizes_Stores_Pushes_Records_Publishes()
    {
        Harness h = Build();
        Guid liveStream = Guid.Parse("019f2a00-5555-7000-8000-000000000042");

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(
            Speak("  hello world  ", streamId: liveStream)
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

        // A truthful usage-ledger row, stamped with when/during-what it played.
        TtsUsageRecord usage = await h.Db.TtsUsageRecords.SingleAsync();
        usage.BroadcasterId.Should().Be(Tenant);
        usage.UserId.Should().Be(Viewer);
        usage.CharacterCount.Should().Be("hello world".Length);
        usage.Provider.Should().Be("edge");
        usage.VoiceId.Should().Be("default-voice");
        usage.WasCensored.Should().BeFalse();
        usage.WasModApproved.Should().BeNull("no moderator was involved on the direct path");
        usage.StreamId.Should().Be(liveStream);
        usage.OccurredAt.Should().Be(T0);

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
    public async Task RequestSpeakAsync_CensorEnabled_SynthesizesMaskedText()
    {
        Harness h = Build(censorEnabled: true);

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(
            Speak("you piece of shit")
        );

        result.IsSuccess.Should().BeTrue();
        // The mild swear is masked BEFORE synthesis — the provider (and thus the overlay) never gets the raw word.
        await h
            .Tts.Received(1)
            .SynthesizeAsync("you piece of s***", "default-voice", Arg.Any<CancellationToken>());
        await h
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<TtsUtteranceDispatchedEvent>(e => e.Text == "you piece of s***"),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.TtsUsageRecords.SingleAsync())
            .WasCensored.Should()
            .BeTrue("the ledger records that the censor altered the spoken text");
    }

    [Fact]
    public async Task RequestSpeakAsync_CensorDisabled_SpeaksRawText()
    {
        Harness h = Build(censorEnabled: false);

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(
            Speak("this is crap")
        );

        result.IsSuccess.Should().BeTrue();
        await h
            .Tts.Received(1)
            .SynthesizeAsync("this is crap", "default-voice", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestSpeakAsync_ModApprovalRequired_QueuesInsteadOfPlaying()
    {
        Harness h = Build(modApprovalRequired: true);

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(Speak("hello there"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Disposition.Should().Be(TtsDispatchDisposition.Queued);

        // Nothing is spoken, played, or ledgered while it waits for a moderator.
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

        TtsApprovalQueueEntry entry = await h.Db.TtsApprovalQueueEntries.SingleAsync();
        entry.BroadcasterId.Should().Be(Tenant);
        entry.Status.Should().Be("pending");
        entry.OriginalText.Should().Be("hello there");
        entry.RequestedByTwitchUserId.Should().Be(Viewer);
        entry.ExpiresAt.Should().BeAfter(entry.CreatedAt);

        await h
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<TtsUtteranceQueuedEvent>(e =>
                    e.QueueEntryId == entry.Id && e.OriginalText == "hello there"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RequestSpeakAsync_ModApprovalWithCensor_StoresBothTexts()
    {
        Harness h = Build(modApprovalRequired: true, censorEnabled: true);

        await h.Service.RequestSpeakAsync(Speak("this is crap"));

        TtsApprovalQueueEntry entry = await h.Db.TtsApprovalQueueEntries.SingleAsync();
        entry.OriginalText.Should().Be("this is crap");
        entry.CensoredText.Should().Be("this is c***");
        entry.WasCensored.Should().BeTrue();
    }

    [Fact]
    public async Task ApproveAsync_SynthesizesCensoredText_PlaysAndMarksApproved()
    {
        Harness h = Build();
        Guid reviewer = Guid.Parse("019f2a00-3333-7000-8000-000000000009");
        Guid liveStream = Guid.Parse("019f2a00-5555-7000-8000-000000000043");
        TtsApprovalQueueEntry entry = SeedPending(
            h,
            original: "raw message",
            censored: "raw m*****e",
            voice: "queued-voice",
            streamId: liveStream
        );

        Result result = await h.Service.ApproveAsync(Tenant, entry.Id, reviewer);

        result.IsSuccess.Should().BeTrue();
        // The censored text (what the moderator reviewed) is what gets synthesized + played.
        await h
            .Tts.Received(1)
            .SynthesizeAsync("raw m*****e", "queued-voice", Arg.Any<CancellationToken>());
        await h
            .Overlay.Received(1)
            .PlaySoundAsync(Tenant, Arg.Any<SoundPlaybackDto>(), Arg.Any<CancellationToken>());

        // The ledger row carries the approval provenance from the queue entry.
        TtsUsageRecord usage = await h.Db.TtsUsageRecords.SingleAsync();
        usage.WasModApproved.Should().BeTrue("a moderator released this utterance");
        usage.WasCensored.Should().BeTrue();
        usage.StreamId.Should().Be(liveStream);
        usage.OccurredAt.Should().Be(T0);

        TtsApprovalQueueEntry updated = await h.Db.TtsApprovalQueueEntries.SingleAsync();
        updated.Status.Should().Be("approved");
        updated.ReviewedByUserId.Should().Be(reviewer);
        updated.ReviewedAt.Should().NotBeNull();

        await h
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<TtsUtteranceReviewedEvent>(e =>
                    e.QueueEntryId == entry.Id && e.Decision == "approved"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ApproveAsync_NoPendingEntry_ReturnsNotFound()
    {
        Harness h = Build();

        Result result = await h.Service.ApproveAsync(Tenant, Guid.NewGuid(), Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task RejectAsync_MarksRejected_WithoutSynthOrPlay()
    {
        Harness h = Build();
        Guid reviewer = Guid.Parse("019f2a00-3333-7000-8000-00000000000a");
        TtsApprovalQueueEntry entry = SeedPending(h, original: "nope", censored: null, voice: "v");

        Result result = await h.Service.RejectAsync(Tenant, entry.Id, reviewer);

        result.IsSuccess.Should().BeTrue();
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

        TtsApprovalQueueEntry updated = await h.Db.TtsApprovalQueueEntries.SingleAsync();
        updated.Status.Should().Be("rejected");
        updated.ReviewedByUserId.Should().Be(reviewer);
        await h
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<TtsUtteranceReviewedEvent>(e =>
                    e.QueueEntryId == entry.Id && e.Decision == "rejected"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetPendingQueueAsync_ReturnsOnlyPending_NewestFirst()
    {
        Harness h = Build();
        DateTime baseTime = new(2026, 7, 12, 0, 0, 0, DateTimeKind.Utc);
        TtsApprovalQueueEntry older = SeedPending(h, "older", null, "v", createdAt: baseTime);
        TtsApprovalQueueEntry newer = SeedPending(
            h,
            "newer",
            null,
            "v",
            createdAt: baseTime.AddMinutes(5)
        );
        TtsApprovalQueueEntry approved = SeedPending(h, "already-approved", null, "v");
        approved.Status = "approved";
        await h.Db.SaveChangesAsync();

        Result<PagedList<TtsQueueEntryDto>> result = await h.Service.GetPendingQueueAsync(
            Tenant,
            1,
            25
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2); // the approved one is excluded
        result.Value.Items.Select(i => i.OriginalText).Should().ContainInOrder("newer", "older"); // newest-first
        result.Value.Items[0].Id.Should().Be(newer.Id);
    }

    private static TtsApprovalQueueEntry SeedPending(
        Harness h,
        string original,
        string? censored,
        string voice,
        DateTime createdAt = default,
        Guid? streamId = null
    )
    {
        TtsApprovalQueueEntry entry = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Tenant,
            RequestedByUserId = Guid.Empty,
            RequestedByTwitchUserId = Viewer,
            RequestedByDisplayName = "Viewer",
            OriginalText = original,
            CensoredText = censored,
            WasCensored = censored is not null,
            VoiceId = voice,
            Provider = "edge",
            Status = "pending",
            StreamId = streamId,
            ExpiresAt = createdAt == default ? default : createdAt.AddMinutes(10),
            CreatedAt = createdAt,
        };
        h.Db.TtsApprovalQueueEntries.Add(entry);
        h.Db.SaveChanges();
        return entry;
    }

    [Fact]
    public async Task RequestSpeakAsync_ByokMode_SynthesizesOnTheChannelsOwnProvider()
    {
        Harness h = Build(mode: "byok", defaultProvider: "azure");
        ITtsProvider byok = Substitute.For<ITtsProvider>();
        byok.SynthesizeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
                Task.FromResult(
                    new TtsSynthesisResult
                    {
                        AudioData = [9, 9, 9],
                        DurationMs = 900,
                        Provider = "azure",
                        VoiceId = ci.ArgAt<string>(1),
                        ContentHash = "hash",
                    }
                )
            );
        h.ByokProviders.CreateForChannelAsync(Tenant, "azure", Arg.Any<CancellationToken>())
            .Returns(Result.Success(byok));

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(Speak("hello"));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        // The BYOK provider did the work; the shared service was never touched.
        await byok.Received(1)
            .SynthesizeAsync("hello", "default-voice", Arg.Any<CancellationToken>());
        await h
            .Tts.DidNotReceive()
            .SynthesizeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        // The ledger records the provider that actually spoke.
        (await h.Db.TtsUsageRecords.SingleAsync())
            .Provider.Should()
            .Be("azure");
    }

    [Fact]
    public async Task RequestSpeakAsync_ByokKeyUnusable_RejectsWithoutLedger()
    {
        Harness h = Build(mode: "byok", defaultProvider: "elevenlabs");
        h.ByokProviders.CreateForChannelAsync(Tenant, "elevenlabs", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ITtsProvider>("The DEK was crypto-shredded.", "KEY_DESTROYED"));

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(Speak("hello"));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SERVICE_UNAVAILABLE");
        (await h.Db.TtsUsageRecords.CountAsync()).Should().Be(0);
        await h
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<TtsUtteranceRejectedEvent>(e => e.Reason == "byok_unavailable"),
                Arg.Any<CancellationToken>()
            );
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

    [Fact]
    public async Task RequestSpeakAsync_ClientEdge_PushesOverlayPayload_WithoutServerSynthesis()
    {
        Harness h = Build(mode: "client_edge", defaultProvider: "edge");
        Guid liveStream = Guid.Parse("019f2a00-5555-7000-8000-000000000099");

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(
            Speak("  hello world  ", streamId: liveStream)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Disposition.Should().Be(TtsDispatchDisposition.Dispatched);
        result.Value.VoiceId.Should().Be("default-voice");
        result.Value.CharacterCount.Should().Be("hello world".Length); // trimmed
        result.Value.PlaybackUrl.Should().BeNull("client-edge ships no server audio URL");

        // The resolved voice + censored text is pushed to the overlay for the widget to render edge-side.
        await h
            .TtsOverlay.Received(1)
            .SpeakAsync(
                Tenant,
                Arg.Is<TtsOverlaySpeakDto>(p =>
                    p.Text == "hello world" && p.VoiceId == "default-voice" && p.Provider == "edge"
                ),
                Arg.Any<CancellationToken>()
            );

        // The server synthesizes / stores / plays NOTHING on the edge plane.
        await h
            .Tts.DidNotReceive()
            .SynthesizeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h
            .Store.DidNotReceive()
            .PutAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<System.IO.Stream>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await h
            .Overlay.DidNotReceive()
            .PlaySoundAsync(
                Arg.Any<Guid>(),
                Arg.Any<SoundPlaybackDto>(),
                Arg.Any<CancellationToken>()
            );

        // A truthful usage-ledger row still lands, stamped with the edge provider + when/during-what.
        TtsUsageRecord usage = await h.Db.TtsUsageRecords.SingleAsync();
        usage.BroadcasterId.Should().Be(Tenant);
        usage.UserId.Should().Be(Viewer);
        usage.CharacterCount.Should().Be("hello world".Length);
        usage.Provider.Should().Be("edge");
        usage.VoiceId.Should().Be("default-voice");
        usage.StreamId.Should().Be(liveStream);
        usage.OccurredAt.Should().Be(T0);

        await h
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<TtsUtteranceDispatchedEvent>(e =>
                    e.DispatchMode == "client_edge"
                    && e.ContentHash == null
                    && e.VoiceId == "default-voice"
                    && e.Text == "hello world"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RequestSpeakAsync_ClientEdge_CensorsTextBeforePushingToOverlay()
    {
        Harness h = Build(mode: "client_edge", censorEnabled: true);

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(
            Speak("you piece of shit")
        );

        result.IsSuccess.Should().BeTrue();
        // The overlay (and thus the widget) only ever receives the masked text — never the raw word.
        await h
            .TtsOverlay.Received(1)
            .SpeakAsync(
                Tenant,
                Arg.Is<TtsOverlaySpeakDto>(p => p.Text == "you piece of s***"),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.TtsUsageRecords.SingleAsync())
            .WasCensored.Should()
            .BeTrue("the ledger records that the censor altered the spoken text");
    }

    [Fact]
    public async Task RequestSpeakAsync_ClientEdge_DisabledChannel_RejectsBeforeAnyPush()
    {
        Harness h = Build(mode: "client_edge", enabled: false);

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(Speak("hello"));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FEATURE_DISABLED");
        await h
            .TtsOverlay.DidNotReceive()
            .SpeakAsync(
                Arg.Any<Guid>(),
                Arg.Any<TtsOverlaySpeakDto>(),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.TtsUsageRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RequestSpeakAsync_ClientEdge_OverCap_RejectsBeforeAnyPush()
    {
        Harness h = Build(mode: "client_edge", maxLength: 10);

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(
            Speak("this is definitely longer than ten characters")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        await h
            .TtsOverlay.DidNotReceive()
            .SpeakAsync(
                Arg.Any<Guid>(),
                Arg.Any<TtsOverlaySpeakDto>(),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.TtsUsageRecords.CountAsync()).Should().Be(0);
    }

    // ── Pronunciation lexicon (tts.md) — applied right before synthesis/push ──

    [Fact]
    public async Task RequestSpeakAsync_AppliesLexicon_BeforeSynthesis_AndLedgersTheSpokenText()
    {
        Harness h = Build();
        h.Db.TtsLexiconEntries.Add(
            new TtsLexiconEntry
            {
                BroadcasterId = Tenant,
                Phrase = "JD",
                Replacement = "Jaydee",
                MatchKind = "word",
            }
        );
        await h.Db.SaveChangesAsync();

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(
            Speak("jd says hi to JDx")
        );

        // Whole-word, case-insensitive: "jd" is rewritten, "JDx" is not.
        result.IsSuccess.Should().BeTrue();
        await h
            .Tts.Received(1)
            .SynthesizeAsync(
                "Jaydee says hi to JDx",
                "default-voice",
                Arg.Any<CancellationToken>()
            );
        TtsUsageRecord ledger = await h.Db.TtsUsageRecords.SingleAsync();
        ledger.CharacterCount.Should().Be("Jaydee says hi to JDx".Length);
    }

    [Fact]
    public async Task RequestSpeakAsync_ClientEdge_PushesTheLexiconAppliedText()
    {
        Harness h = Build(mode: "client_edge");
        h.Db.TtsLexiconEntries.Add(
            new TtsLexiconEntry
            {
                BroadcasterId = Tenant,
                Phrase = "brb",
                Replacement = "be right back",
                MatchKind = "word",
            }
        );
        await h.Db.SaveChangesAsync();

        Result<TtsDispatchOutcome> result = await h.Service.RequestSpeakAsync(Speak("brb chat"));

        result.IsSuccess.Should().BeTrue();
        await h
            .TtsOverlay.Received(1)
            .SpeakAsync(
                Tenant,
                Arg.Is<TtsOverlaySpeakDto>(dto => dto.Text == "be right back chat"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RequestSpeakAsync_LexiconWrite_InvalidatesTheCache_SoTheNextDispatchReflectsIt()
    {
        Harness h = Build();

        // First dispatch primes the (empty) lexicon cache.
        (await h.Service.RequestSpeakAsync(Speak("gg chat")))
            .IsSuccess.Should()
            .BeTrue();
        await h
            .Tts.Received(1)
            .SynthesizeAsync("gg chat", "default-voice", Arg.Any<CancellationToken>());

        // Writing a rule through the service must evict the cache — the very next dispatch speaks the new form.
        (
            await h.Lexicon.CreateAsync(
                Tenant,
                new UpsertTtsLexiconEntryDto { Phrase = "gg", Replacement = "good game" }
            )
        )
            .IsSuccess.Should()
            .BeTrue();

        (await h.Service.RequestSpeakAsync(Speak("gg chat"))).IsSuccess.Should().BeTrue();
        await h
            .Tts.Received(1)
            .SynthesizeAsync("good game chat", "default-voice", Arg.Any<CancellationToken>());
    }
}
