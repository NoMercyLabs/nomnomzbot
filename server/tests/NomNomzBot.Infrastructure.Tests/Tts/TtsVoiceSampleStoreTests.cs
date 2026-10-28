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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// A voice preview plays a pre-made clip. These tests assert what is stored on disk, how often a provider is
/// called, and which voices the background pass makes.
/// </summary>
public sealed class TtsVoiceSampleStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nnz-tts-samples-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task A_saved_clip_comes_back_with_its_audio_and_facts_and_an_unknown_voice_has_none()
    {
        DiskTtsVoiceSampleStore sut = new(_directory);

        await sut.SaveAsync("en-US-AriaNeural", new([1, 2, 3], 1500, "en-US-AriaNeural", "edge"));

        TtsResult? clip = await sut.GetAsync("en-us-arianeural");

        clip.Should().NotBeNull();
        clip!.AudioData.Should().Equal(1, 2, 3);
        clip.DurationMs.Should().Be(1500);
        clip.Provider.Should().Be("edge");
        (await sut.GetAsync("nl-NL-ColetteNeural")).Should().BeNull();
    }

    [Fact]
    public async Task A_voice_id_that_looks_like_a_path_stays_inside_the_samples_folder()
    {
        DiskTtsVoiceSampleStore sut = new(_directory);

        await sut.SaveAsync("..\\..\\escape", new([9], 10, "x", "edge"));

        Directory.GetFiles(_directory).Should().HaveCount(2);
        Directory.GetFiles(Path.GetDirectoryName(_directory)!, "escape*").Should().BeEmpty();
    }

    [Fact]
    public async Task The_voice_introduction_is_made_once_and_played_from_storage_after_that()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        ITtsService tts = Substitute.For<ITtsService>();
        tts.SynthesizeAsync(Arg.Any<string>(), "voice", ct: Arg.Any<CancellationToken>())
            .Returns(new TtsResult([7, 7], 900, "voice", "edge"));
        DiskTtsVoiceSampleStore store = new(_directory);
        TtsConfigService sut = NewConfig(db, tts, store);

        Result<TtsTestResultDto> first = await sut.TestVoiceAsync(
            Guid.NewGuid(),
            new() { VoiceId = "voice" }
        );
        Result<TtsTestResultDto> second = await sut.TestVoiceAsync(
            Guid.NewGuid(),
            new() { VoiceId = "voice" }
        );

        await tts.Received(1)
            .SynthesizeAsync(Arg.Any<string>(), "voice", ct: Arg.Any<CancellationToken>());
        second.Value.AudioBase64.Should().Be(first.Value.AudioBase64);
        second.Value.DurationMs.Should().Be(900);
        (await store.GetAsync("voice"))!.AudioData.Should().Equal(7, 7);
    }

    [Fact]
    public async Task A_line_the_person_typed_is_spoken_live_and_never_stored()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        ITtsService tts = Substitute.For<ITtsService>();
        tts.SynthesizeAsync(Arg.Any<string>(), "voice", ct: Arg.Any<CancellationToken>())
            .Returns(new TtsResult([5], 100, "voice", "edge"));
        DiskTtsVoiceSampleStore store = new(_directory);
        TtsConfigService sut = NewConfig(db, tts, store);

        await sut.TestVoiceAsync(Guid.NewGuid(), new() { Text = "my line", VoiceId = "voice" });
        await sut.TestVoiceAsync(Guid.NewGuid(), new() { Text = "my line", VoiceId = "voice" });

        await tts.Received(2).SynthesizeAsync("my line", "voice", ct: Arg.Any<CancellationToken>());
        (await store.GetAsync("voice")).Should().BeNull();
    }

    [Fact]
    public async Task The_background_pass_makes_only_the_free_voices_that_have_no_clip()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        db.TtsVoices.AddRange(
            Voice("edge-has", "edge"),
            Voice("edge-missing", "edge"),
            Voice("paid", "elevenlabs")
        );
        await db.SaveChangesAsync();
        ITtsService tts = Substitute.For<ITtsService>();
        tts.SynthesizeAsync(Arg.Any<string>(), Arg.Any<string>(), ct: Arg.Any<CancellationToken>())
            .Returns(ci => new TtsResult([4], 200, ci.ArgAt<string>(1), "edge"));
        DiskTtsVoiceSampleStore store = new(_directory);
        await store.SaveAsync("edge-has", new([1], 100, "edge-has", "edge"));
        TtsVoiceSampleWarmer sut = NewWarmer(NewConfig(db, tts, store), db, store);

        int made = await sut.WarmAsync(TimeSpan.Zero, CancellationToken.None);

        made.Should().Be(1);
        await tts.Received(1)
            .SynthesizeAsync(Arg.Any<string>(), "edge-missing", ct: Arg.Any<CancellationToken>());
        await tts.DidNotReceive()
            .SynthesizeAsync(Arg.Any<string>(), "paid", ct: Arg.Any<CancellationToken>());
        (await store.GetAsync("edge-missing")).Should().NotBeNull();
        (await store.GetAsync("paid")).Should().BeNull();
    }

    [Fact]
    public async Task The_background_pass_ends_after_five_failures_in_a_row()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        for (int i = 0; i < 9; i++)
            db.TtsVoices.Add(Voice("edge-" + i, "edge"));
        await db.SaveChangesAsync();
        ITtsService tts = Substitute.For<ITtsService>();
        tts.SynthesizeAsync(Arg.Any<string>(), Arg.Any<string>(), ct: Arg.Any<CancellationToken>())
            .Returns<TtsResult>(_ => throw new InvalidOperationException("provider down"));
        DiskTtsVoiceSampleStore store = new(_directory);
        TtsVoiceSampleWarmer sut = NewWarmer(NewConfig(db, tts, store), db, store);

        int made = await sut.WarmAsync(TimeSpan.Zero, CancellationToken.None);

        made.Should().Be(0);
        await tts.Received(TtsVoiceSampleWarmer.MaxFailuresInARow)
            .SynthesizeAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                ct: Arg.Any<CancellationToken>()
            );
    }

    private static NomNomzBot.Domain.Tts.Entities.TtsVoice Voice(string id, string provider) =>
        new()
        {
            Id = id,
            Name = id,
            DisplayName = id,
            Locale = "en-US",
            Gender = "Female",
            Provider = provider,
        };

    private static TtsVoiceSampleWarmer NewWarmer(
        TtsConfigService config,
        TtsTestDbContext db,
        ITtsVoiceSampleStore store
    )
    {
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton<ITtsConfigService>(config);
        return new(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            store,
            NullLogger<TtsVoiceSampleWarmer>.Instance
        );
    }

    private static TtsConfigService NewConfig(
        TtsTestDbContext db,
        ITtsService tts,
        ITtsVoiceSampleStore store
    )
    {
        ITtsLexiconService lexicon = Substitute.For<ITtsLexiconService>();
        lexicon
            .ApplyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.ArgAt<string>(1)));
        return new(
            db,
            tts,
            Substitute.For<IEventBus>(),
            Substitute.For<ISubjectKeyService>(),
            Substitute.For<NomNomzBot.Application.Identity.Services.IUserService>(),
            new PlatformTtsVoiceDefault(db),
            lexicon,
            new SpokenNameFormatter(),
            store
        );
    }
}
