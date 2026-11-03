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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Infrastructure.Sound;
using NomNomzBot.Infrastructure.Tests.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Sound;

/// <summary>
/// Clips stored before the MP3 length fix carry a length 1000 times too short. The one-time repair measures
/// every stored MP3 again from its own bytes and writes the right length, and touches nothing else.
/// </summary>
public sealed class SoundClipDurationRepairTests
{
    private static readonly Guid ChannelA = Guid.Parse("0192c000-0000-7000-8000-00000000a001");
    private static readonly Guid ChannelB = Guid.Parse("0192c000-0000-7000-8000-00000000b001");

    // MPEG-1 Layer 3, 128 kbps, 44.1 kHz frame header, then silence: 16,000 bytes per second.
    private static byte[] ConstantBitrateMp3(int seconds)
    {
        byte[] bytes = new byte[16_000 * seconds];
        bytes[0] = 0xFF;
        bytes[1] = 0xFB;
        bytes[2] = 0x90;
        return bytes;
    }

    private static SoundClip NewClip(
        InMemorySoundClipStore store,
        Guid channel,
        string name,
        byte[]? bytes,
        int durationMs,
        string mimeType = "audio/mpeg"
    )
    {
        string key = $"{channel:N}/{name}";
        if (bytes is not null)
            store.Blobs[key] = bytes;
        return new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = name,
            DisplayName = name,
            StorageKey = key,
            MimeType = mimeType,
            DurationMs = durationMs,
            SizeBytes = bytes?.Length ?? 0,
            CreatedByUserId = Guid.NewGuid(),
        };
    }

    private static async Task<Dictionary<string, int>> DurationsAsync(
        PipelineOptionsTestDbContext db
    ) =>
        await db
            .SoundClips.IgnoreQueryFilters()
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Name, c => c.DurationMs);

    private static SoundClipDurationRepair Build(
        PipelineOptionsTestDbContext db,
        InMemorySoundClipStore store
    ) => new(db, store, NullLogger<SoundClipDurationRepair>.Instance);

    [Fact]
    public async Task An_mp3_stored_with_length_22_ends_with_ten_thousand_for_a_ten_second_file()
    {
        PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        InMemorySoundClipStore store = new();
        db.SoundClips.Add(NewClip(store, ChannelA, "fortuna", ConstantBitrateMp3(10), 22));
        await db.SaveChangesAsync();

        SoundClipDurationRepairReport report = await Build(db, store).RunAsync();

        (await DurationsAsync(db))["fortuna"].Should().Be(10_000);
        report.Should().Be(new SoundClipDurationRepairReport(1, 0, 0));
    }

    [Fact]
    public async Task A_clip_with_the_right_length_is_unchanged()
    {
        PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        InMemorySoundClipStore store = new();
        db.SoundClips.Add(NewClip(store, ChannelA, "good", ConstantBitrateMp3(10), 10_000));
        await db.SaveChangesAsync();

        SoundClipDurationRepairReport report = await Build(db, store).RunAsync();

        (await DurationsAsync(db))["good"].Should().Be(10_000);
        report.Should().Be(new SoundClipDurationRepairReport(0, 1, 0));
    }

    [Fact]
    public async Task An_unreadable_clip_is_skipped_and_the_others_still_update()
    {
        PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        InMemorySoundClipStore store = new();
        db.SoundClips.Add(NewClip(store, ChannelA, "missing", bytes: null, durationMs: 22));
        db.SoundClips.Add(NewClip(store, ChannelA, "noise", TestAudio.NotAudio(), 33));
        db.SoundClips.Add(NewClip(store, ChannelA, "fine", ConstantBitrateMp3(5), 22));
        await db.SaveChangesAsync();

        SoundClipDurationRepairReport report = await Build(db, store).RunAsync();

        Dictionary<string, int> durations = await DurationsAsync(db);
        durations["missing"].Should().Be(22);
        durations["noise"].Should().Be(33);
        durations["fine"].Should().Be(5_000);
        report.Should().Be(new SoundClipDurationRepairReport(1, 0, 2));
    }

    [Fact]
    public async Task A_non_mp3_clip_is_not_touched()
    {
        PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        InMemorySoundClipStore store = new();
        db.SoundClips.Add(
            NewClip(store, ChannelA, "wave", TestAudio.Wav(3), 7, mimeType: "audio/wav")
        );
        await db.SaveChangesAsync();

        SoundClipDurationRepairReport report = await Build(db, store).RunAsync();

        (await DurationsAsync(db))["wave"].Should().Be(7);
        report.Should().Be(new SoundClipDurationRepairReport(0, 0, 0));
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        InMemorySoundClipStore store = new();
        db.SoundClips.Add(NewClip(store, ChannelA, "fortuna", ConstantBitrateMp3(10), 22));
        await db.SaveChangesAsync();
        SoundClipDurationRepair repair = Build(db, store);
        await repair.RunAsync();

        SoundClipDurationRepairReport second = await repair.RunAsync();

        second.Should().Be(new SoundClipDurationRepairReport(0, 1, 0));
        (await DurationsAsync(db))["fortuna"].Should().Be(10_000);
    }

    [Fact]
    public async Task The_hosted_service_repairs_only_when_the_lease_is_granted()
    {
        PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        InMemorySoundClipStore store = new();
        db.SoundClips.Add(NewClip(store, ChannelA, "fortuna", ConstantBitrateMp3(10), 22));
        await db.SaveChangesAsync();
        IRunOnceGuard guard = Substitute.For<IRunOnceGuard>();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton<ISoundClipStore>(store)
            .AddLogging()
            .AddSingleton(guard)
            .AddScoped<SoundClipDurationRepair>()
            .BuildServiceProvider();
        SoundClipDurationRepairHostedService hosted = new(
            provider.GetRequiredService<IServiceScopeFactory>()
        );

        guard
            .TryAcquireAsync(
                SoundClipDurationRepairHostedService.LeaseResourceName,
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns((IAsyncDisposable?)null);
        await hosted.StartAsync(CancellationToken.None);
        (await DurationsAsync(db))["fortuna"].Should().Be(22);

        guard
            .TryAcquireAsync(
                SoundClipDurationRepairHostedService.LeaseResourceName,
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Substitute.For<IAsyncDisposable>());
        await hosted.StartAsync(CancellationToken.None);
        (await DurationsAsync(db))["fortuna"].Should().Be(10_000);
    }

    [Fact]
    public async Task Two_channels_each_get_their_own_length()
    {
        PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        InMemorySoundClipStore store = new();
        db.SoundClips.Add(NewClip(store, ChannelA, "short", ConstantBitrateMp3(4), 22));
        db.SoundClips.Add(NewClip(store, ChannelB, "long", ConstantBitrateMp3(12), 22));
        await db.SaveChangesAsync();

        await Build(db, store).RunAsync();

        Dictionary<string, int> durations = await DurationsAsync(db);
        durations["short"].Should().Be(4_000);
        durations["long"].Should().Be(12_000);
    }
}
