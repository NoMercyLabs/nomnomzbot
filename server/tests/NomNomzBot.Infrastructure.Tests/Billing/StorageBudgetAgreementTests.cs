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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Assets.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Assets;
using NomNomzBot.Infrastructure.Billing;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Sound;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// S-BUDGETS-b6: proves the storage write paths (<see cref="SoundClipService"/>,
/// <see cref="ChannelAssetService"/>) can never disagree with what <c>GET billing/usage</c> shows, because
/// both go through the exact same <see cref="IResourceQuotaService"/> seam over the SAME
/// <c>LimitedResourceRegistry</c> key. Also proves the per-file abuse guard (a fixed safety baseline) survives
/// regardless of tier, and that self-host is never gated by a commercial ceiling.
/// </summary>
public sealed class StorageBudgetAgreementTests
{
    private static readonly Guid Channel = Guid.Parse("0192c000-0000-7000-8000-0000000000b6");
    private static readonly Guid Actor = Guid.Parse("0192c000-0000-7000-8000-0000000000aa");

    private static (
        SoundClipService SoundClips,
        ChannelAssetService Assets,
        ResourceQuotaService Quota,
        AuthDbContext Db
    ) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        BillingTierService tiers = new(db);
        UsageMeteringService metering = new(
            db,
            tiers,
            new RecordingEventBus(),
            new FakeTimeProvider()
        );
        ResourceQuotaService quota = new(tiers, metering, db);
        SoundClipService soundClips = new(
            db,
            new FakeSoundClipStore(),
            new FakeOverlayNotifier(),
            new FakeChannelRegistry(),
            quota,
            new PipelineStepReferenceScanner(db)
        );
        ChannelAssetService assets = new(
            db,
            new FakeAssetStore(),
            quota,
            new PipelineStepReferenceScanner(db)
        );
        return (soundClips, assets, quota, db);
    }

    private static void SeedSaasChannelWithTier(AuthDbContext db, long storageLimitBytes)
    {
        db.Channels.Add(
            new()
            {
                Id = Channel,
                TwitchChannelId = "t1",
                Name = "chan",
                NameNormalized = "chan",
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
            }
        );
        BillingTier tier = new()
        {
            Key = "base",
            DisplayName = "Base",
            PriceCents = 399,
            Currency = "usd",
            IsPublic = true,
            SortOrder = 10,
        };
        db.BillingTiers.Add(tier);
        db.TierLimits.Add(
            new()
            {
                TierId = tier.Id,
                LimitKey = "sound_clip_storage_bytes",
                LimitValue = storageLimitBytes,
            }
        );
        db.TierLimits.Add(
            new()
            {
                TierId = tier.Id,
                LimitKey = "channel_asset_storage_bytes",
                LimitValue = storageLimitBytes,
            }
        );
    }

    private static void SeedSelfHostChannel(AuthDbContext db) =>
        db.Channels.Add(
            new()
            {
                Id = Channel,
                TwitchChannelId = "t1",
                Name = "chan",
                NameNormalized = "chan",
                DeploymentMode = AuthEnums.DeploymentMode.SelfHostFull,
            }
        );

    private static byte[] Mp3Bytes(int totalLength)
    {
        byte[] bytes = new byte[totalLength];
        bytes[0] = 0x49; // 'I'
        bytes[1] = 0x44; // 'D'
        bytes[2] = 0x33; // '3'
        for (int i = 3; i < totalLength; i++)
            bytes[i] = (byte)(i % 251);
        return bytes;
    }

    private static byte[] PngBytes(int totalLength)
    {
        byte[] bytes = new byte[totalLength];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        for (int i = 8; i < totalLength; i++)
            bytes[i] = (byte)(i % 251);
        return bytes;
    }

    private static UploadSoundClipRequest SoundRequest(string name, byte[] payload) =>
        new(name, name, "clip.mp3", "audio/mpeg", new MemoryStream(payload), DefaultVolume: 80);

    private static UploadChannelAssetRequest AssetRequest(string name, byte[] payload) =>
        new(name, name, "asset.png", new MemoryStream(payload));

    // ── Agreement: shown limit == enforced limit ────────────────────────────────

    [Fact]
    public async Task SoundClip_usage_report_limit_equals_the_limit_the_refusal_enforces()
    {
        const long TierLimitBytes = 15L * 1024 * 1024; // 15 MB
        (SoundClipService soundClips, _, ResourceQuotaService quota, AuthDbContext db) = Build();
        SeedSaasChannelWithTier(db, TierLimitBytes);
        await db.SaveChangesAsync();

        // Fill to the tier limit with one 10 MB clip (at the per-file abuse-guard ceiling) plus a 5 MB clip.
        Result<SoundClipDto> first = await soundClips.UploadAsync(
            Channel,
            Actor,
            SoundRequest("clip-a", Mp3Bytes(10 * 1024 * 1024))
        );
        first.IsSuccess.Should().BeTrue();
        Result<SoundClipDto> second = await soundClips.UploadAsync(
            Channel,
            Actor,
            SoundRequest("clip-b", Mp3Bytes(5 * 1024 * 1024))
        );
        second.IsSuccess.Should().BeTrue();

        // The usage report's limit for this key...
        IReadOnlyList<ResourceUsageDto> report = (await quota.GetUsageReportAsync(Channel)).Value;
        ResourceUsageDto usage = report.Single(r => r.LimitKey == "sound_clip_storage_bytes");
        usage.CurrentCount.Should().Be(TierLimitBytes);
        usage.Limit.Should().Be(TierLimitBytes);

        // ...is EXACTLY the limit that refuses the next byte of upload.
        Result<SoundClipDto> third = await soundClips.UploadAsync(
            Channel,
            Actor,
            SoundRequest("clip-c", Mp3Bytes(1024))
        );
        third.IsSuccess.Should().BeFalse();
        third.ErrorCode.Should().Be("CHANNEL_BUDGET_EXCEEDED");
        third.ErrorMessage.Should().Contain($"{usage.Limit / 1024 / 1024} MB");
    }

    [Fact]
    public async Task ChannelAsset_usage_report_limit_equals_the_limit_the_refusal_enforces()
    {
        const long TierLimitBytes = 12L * 1024 * 1024; // 12 MB
        (_, ChannelAssetService assets, ResourceQuotaService quota, AuthDbContext db) = Build();
        SeedSaasChannelWithTier(db, TierLimitBytes);
        await db.SaveChangesAsync();

        Result<ChannelAssetDto> first = await assets.UploadAsync(
            Channel,
            Actor,
            AssetRequest("asset-a", PngBytes(8 * 1024 * 1024))
        );
        first.IsSuccess.Should().BeTrue();
        Result<ChannelAssetDto> second = await assets.UploadAsync(
            Channel,
            Actor,
            AssetRequest("asset-b", PngBytes(4 * 1024 * 1024))
        );
        second.IsSuccess.Should().BeTrue();

        IReadOnlyList<ResourceUsageDto> report = (await quota.GetUsageReportAsync(Channel)).Value;
        ResourceUsageDto usage = report.Single(r => r.LimitKey == "channel_asset_storage_bytes");
        usage.CurrentCount.Should().Be(TierLimitBytes);
        usage.Limit.Should().Be(TierLimitBytes);

        Result<ChannelAssetDto> third = await assets.UploadAsync(
            Channel,
            Actor,
            AssetRequest("asset-c", PngBytes(1024))
        );
        third.IsSuccess.Should().BeFalse();
        third.ErrorCode.Should().Be("CHANNEL_BUDGET_EXCEEDED");
        third.ErrorMessage.Should().Contain($"{usage.Limit / 1024 / 1024} MB");
    }

    // ── Per-file abuse guard survives regardless of tier ────────────────────────

    [Fact]
    public async Task SoundClip_per_file_size_cap_refuses_an_oversized_upload_even_on_a_generous_tier()
    {
        (SoundClipService soundClips, _, _, AuthDbContext db) = Build();
        SeedSaasChannelWithTier(db, 2L * 1024 * 1024 * 1024); // 2 GB — generous, plenty of channel headroom
        await db.SaveChangesAsync();

        Result<SoundClipDto> uploaded = await soundClips.UploadAsync(
            Channel,
            Actor,
            SoundRequest("too-big", Mp3Bytes(11 * 1024 * 1024)) // over the fixed 10 MB per-clip baseline
        );

        uploaded.IsSuccess.Should().BeFalse();
        uploaded.ErrorCode.Should().Be("SIZE_EXCEEDED");
    }

    [Fact]
    public async Task ChannelAsset_per_file_size_cap_refuses_an_oversized_upload_even_on_a_generous_tier()
    {
        (_, ChannelAssetService assets, _, AuthDbContext db) = Build();
        SeedSaasChannelWithTier(db, 2L * 1024 * 1024 * 1024); // 2 GB — generous, plenty of channel headroom
        await db.SaveChangesAsync();

        Result<ChannelAssetDto> uploaded = await assets.UploadAsync(
            Channel,
            Actor,
            AssetRequest("too-big", PngBytes(9 * 1024 * 1024)) // over the fixed 8 MB per-asset baseline
        );

        uploaded.IsSuccess.Should().BeFalse();
        uploaded.ErrorCode.Should().Be("SIZE_EXCEEDED");
    }

    // ── Self-host: safety baseline only, never a commercial ceiling ────────────

    [Fact]
    public async Task SoundClip_self_host_refuses_only_the_per_file_baseline_never_a_channel_budget()
    {
        (SoundClipService soundClips, _, _, AuthDbContext db) = Build();
        SeedSelfHostChannel(db);
        await db.SaveChangesAsync();

        // Well past any seeded SaaS tier's channel ceiling (2 GB) worth of clips, each under the per-file
        // baseline — self-host must accept every one; disk is the operator's own.
        for (int i = 0; i < 5; i++)
        {
            Result<SoundClipDto> uploaded = await soundClips.UploadAsync(
                Channel,
                Actor,
                SoundRequest($"clip-{i}", Mp3Bytes(9 * 1024 * 1024))
            );
            uploaded.IsSuccess.Should().BeTrue();
        }

        // The per-file abuse guard still fires — self-host is never crippled below it, but never above it either.
        Result<SoundClipDto> oversized = await soundClips.UploadAsync(
            Channel,
            Actor,
            SoundRequest("clip-oversized", Mp3Bytes(11 * 1024 * 1024))
        );
        oversized.IsSuccess.Should().BeFalse();
        oversized.ErrorCode.Should().Be("SIZE_EXCEEDED");
    }

    [Fact]
    public async Task ChannelAsset_self_host_refuses_only_the_per_file_baseline_never_a_channel_budget()
    {
        (_, ChannelAssetService assets, _, AuthDbContext db) = Build();
        SeedSelfHostChannel(db);
        await db.SaveChangesAsync();

        for (int i = 0; i < 5; i++)
        {
            Result<ChannelAssetDto> uploaded = await assets.UploadAsync(
                Channel,
                Actor,
                AssetRequest($"asset-{i}", PngBytes(7 * 1024 * 1024))
            );
            uploaded.IsSuccess.Should().BeTrue();
        }

        Result<ChannelAssetDto> oversized = await assets.UploadAsync(
            Channel,
            Actor,
            AssetRequest("asset-oversized", PngBytes(9 * 1024 * 1024))
        );
        oversized.IsSuccess.Should().BeFalse();
        oversized.ErrorCode.Should().Be("SIZE_EXCEEDED");
    }

    // ── Refusal message carries the real numbers ────────────────────────────────

    [Fact]
    public async Task ChannelAsset_budget_refusal_names_the_actual_used_and_limit_megabytes()
    {
        const long TierLimitBytes = 10L * 1024 * 1024; // 10 MB
        (_, ChannelAssetService assets, _, AuthDbContext db) = Build();
        SeedSaasChannelWithTier(db, TierLimitBytes);
        await db.SaveChangesAsync();

        Result<ChannelAssetDto> filled = await assets.UploadAsync(
            Channel,
            Actor,
            AssetRequest("asset-a", PngBytes(8 * 1024 * 1024)) // 8 MB used
        );
        filled.IsSuccess.Should().BeTrue();

        Result<ChannelAssetDto> refused = await assets.UploadAsync(
            Channel,
            Actor,
            AssetRequest("asset-b", PngBytes(4 * 1024 * 1024)) // would push to 12 MB, over the 10 MB limit
        );

        refused.IsSuccess.Should().BeFalse();
        refused.ErrorCode.Should().Be("CHANNEL_BUDGET_EXCEEDED");
        // Names the would-be USED total (12.0 MB) and the LIMIT (10 MB), not a bare error code.
        refused.ErrorMessage.Should().Contain("12.0 MB");
        refused.ErrorMessage.Should().Contain("10 MB");
    }

    // ── Test doubles ─────────────────────────────────────────────────────────────

    private sealed class FakeSoundClipStore : ISoundClipStore
    {
        public Task<Result<string>> PutAsync(
            Guid broadcasterId,
            string fileName,
            System.IO.Stream content,
            string mimeType,
            CancellationToken ct = default
        ) => Task.FromResult(Result<string>.Success($"key/{Guid.NewGuid()}"));

        public Task<Result<System.IO.Stream>> OpenAsync(
            string storageKey,
            CancellationToken ct = default
        ) => Task.FromResult(Result<System.IO.Stream>.Success(new MemoryStream()));

        public Task<Result> DeleteAsync(string storageKey, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<string>> GetPlaybackUrlAsync(
            string storageKey,
            CancellationToken ct = default
        ) => Task.FromResult(Result<string>.Success($"/play/{storageKey}"));
    }

    private sealed class FakeOverlayNotifier : ISoundClipOverlayNotifier
    {
        public Task PlaySoundAsync(
            Guid broadcasterId,
            SoundPlaybackDto playback,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task StopSoundAsync(
            Guid broadcasterId,
            string? handle,
            bool all,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }

    private sealed class FakeChannelRegistry : IChannelRegistry
    {
        public IReadOnlyCollection<ChannelContext> GetAll() => [];

        public IReadOnlyCollection<ChannelContext> GetLiveChannels() => [];

        public int Count => 0;

        public ChannelContext? Get(Guid broadcasterId) => null;

        public Task<ChannelContext> GetOrCreateAsync(
            Guid broadcasterId,
            string twitchChannelId,
            string channelName,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task InvalidateCommandsAsync(Guid broadcasterId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task InvalidateBuiltinsAsync(Guid broadcasterId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task InvalidateSettingsAsync(Guid broadcasterId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task InvalidateChatTriggersAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task InvalidateModerationStandingsAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task InvalidateSoundTriggersAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task RemoveAsync(Guid broadcasterId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeAssetStore : IChannelAssetStore
    {
        public Task<Result<string>> PutAsync(
            Guid broadcasterId,
            string fileName,
            System.IO.Stream content,
            string mimeType,
            CancellationToken ct = default
        ) => Task.FromResult(Result<string>.Success($"key/{Guid.NewGuid()}"));

        public Task<Result<System.IO.Stream>> OpenAsync(
            string storageKey,
            CancellationToken ct = default
        ) => Task.FromResult(Result<System.IO.Stream>.Success(new MemoryStream()));

        public Task<Result> DeleteAsync(string storageKey, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }
}
