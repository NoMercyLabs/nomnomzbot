// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Assets;
using NomNomzBot.Infrastructure.Chat.YouTube;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Tests.Assets;
using NomNomzBot.Infrastructure.Tests.Marketplace;

namespace NomNomzBot.Infrastructure.Tests.Chat.YouTube;

/// <summary>
/// S-YT-STICKER-IMAGE (owner correction) — stickers are just custom image assets, the same shape a Voice
/// Trigger's <c>StickerAssetId</c> already takes. Proves <see cref="YouTubeSuperStickerAssetResolver"/>
/// downloads a sticker's CDN image ONCE, stores it through the SAME <c>IChannelAssetService.UploadAsync</c>
/// path an operator's own upload takes, and answers with THAT asset's own URL — never the raw
/// googleusercontent.com CDN URL. A repeat of the same (channel, stickerId) reuses the cached asset rather
/// than downloading or uploading again, and every failure (unresolvable sticker, download failure) degrades
/// to null without throwing.
/// </summary>
public sealed class YouTubeSuperStickerAssetResolverTests
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-00000000b001");
    private static readonly Guid Owner = Guid.Parse("0192b000-0000-7000-8000-00000000b0aa");

    [Fact]
    public async Task A_known_sticker_downloads_once_and_resolves_to_OUR_OWN_asset_url()
    {
        (
            YouTubeSuperStickerAssetResolver resolver,
            MarketplaceTestDbContext db,
            RecordingHandler handler,
            _
        ) = await BuildAsync(
            new FakeCdnResolver("sticker-42", "https://lh3.googleusercontent.com/x.png")
        );
        await using MarketplaceTestDbContext _db = db;

        string? url = await resolver.ResolveAssetUrlAsync(Channel, "sticker-42");

        url.Should().NotBeNull();
        url!
            .Should()
            .NotContain("googleusercontent.com", "the alert must never carry Google's raw CDN URL");
        handler.RequestCount.Should().Be(1);
        (await db.ChannelAssets.CountAsync(a => a.BroadcasterId == Channel)).Should().Be(1);
    }

    [Fact]
    public async Task A_repeat_of_the_same_sticker_for_the_same_channel_reuses_the_stored_asset()
    {
        (
            YouTubeSuperStickerAssetResolver resolver,
            MarketplaceTestDbContext db,
            RecordingHandler handler,
            _
        ) = await BuildAsync(
            new FakeCdnResolver("sticker-42", "https://lh3.googleusercontent.com/x.png")
        );
        await using MarketplaceTestDbContext _db = db;

        string? first = await resolver.ResolveAssetUrlAsync(Channel, "sticker-42");
        string? second = await resolver.ResolveAssetUrlAsync(Channel, "sticker-42");

        second
            .Should()
            .Be(first, "the second sighting must answer from the cache, not a fresh upload");
        handler
            .RequestCount.Should()
            .Be(1, "no second CDN download for a sticker already resolved");
        (await db.ChannelAssets.CountAsync(a => a.BroadcasterId == Channel))
            .Should()
            .Be(1, "no duplicate asset row from the repeat");
    }

    [Fact]
    public async Task An_unresolvable_sticker_id_degrades_to_null_without_throwing_or_writing_an_asset()
    {
        (
            YouTubeSuperStickerAssetResolver resolver,
            MarketplaceTestDbContext db,
            RecordingHandler _,
            _
        ) = await BuildAsync(
            new FakeCdnResolver("known-sticker", "https://lh3.googleusercontent.com/x.png")
        );
        await using MarketplaceTestDbContext _db = db;

        string? url = await resolver.ResolveAssetUrlAsync(Channel, "unknown-sticker");

        url.Should().BeNull();
        (await db.ChannelAssets.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_failed_cdn_download_degrades_to_null_without_throwing_or_writing_an_asset()
    {
        (
            YouTubeSuperStickerAssetResolver resolver,
            MarketplaceTestDbContext db,
            RecordingHandler _,
            _
        ) = await BuildAsync(
            new FakeCdnResolver("sticker-42", "https://lh3.googleusercontent.com/x.png"),
            downloadStatus: HttpStatusCode.ServiceUnavailable
        );
        await using MarketplaceTestDbContext _db = db;

        string? url = await resolver.ResolveAssetUrlAsync(Channel, "sticker-42");

        url.Should().BeNull();
        (await db.ChannelAssets.CountAsync()).Should().Be(0);
    }

    private static async Task<(
        YouTubeSuperStickerAssetResolver Resolver,
        MarketplaceTestDbContext Db,
        RecordingHandler Handler,
        FakeAssetStore Store
    )> BuildAsync(FakeCdnResolver cdnResolver, HttpStatusCode downloadStatus = HttpStatusCode.OK)
    {
        MarketplaceTestDbContext db = MarketplaceTestDbContext.New();
        // The test context ignores ChannelAsset.CreatedByUser and Channel.User (no User DbSet mapped), so
        // OwnerUserId/CreatedByUserId are plain, unconstrained Guid columns here — no User row needed.
        db.Channels.Add(
            new Channel
            {
                Id = Channel,
                Name = "sticker-test-channel",
                NameNormalized = "sticker-test-channel",
                OwnerUserId = Owner,
            }
        );
        await db.SaveChangesAsync();

        FakeAssetStore store = new();
        ChannelAssetService assets = new(
            db,
            store,
            new AlwaysAllowQuota(),
            new PipelineStepReferenceScanner(db)
        );
        RecordingHandler handler = new(downloadStatus, ChannelAssetServiceTests.PngBytes());

        YouTubeSuperStickerAssetResolver resolver = new(
            cdnResolver,
            new SingleClientFactory(handler),
            assets,
            db,
            new YouTubeSuperStickerAssetCache(),
            NullLogger<YouTubeSuperStickerAssetResolver>.Instance
        );

        return (resolver, db, handler, store);
    }

    /// <summary>Resolves ONE known sticker id to a fixed CDN URL; every other id resolves to null.</summary>
    private sealed class FakeCdnResolver(string knownStickerId, string cdnUrl)
        : IYouTubeSuperStickerImageResolver
    {
        public Task<string?> ResolveAsync(
            string stickerId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(stickerId == knownStickerId ? cdnUrl : null);
    }

    /// <summary>Answers every request with a fixed status + body, and counts how many it saw.</summary>
    private sealed class RecordingHandler(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new ByteArrayContent(body) }
            );
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Reproduces an always-allow quota check without a full billing-tier fixture.</summary>
    private sealed class AlwaysAllowQuota : IResourceQuotaService
    {
        public Task<Result<QuotaCheckDto>> CheckAsync(
            Guid broadcasterId,
            string limitKey,
            long resultingCount,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Result<QuotaCheckDto>.Success(
                    new(true, limitKey, resultingCount, long.MaxValue, long.MaxValue)
                )
            );

        public Task<Result<long>> GetCurrentCountAsync(
            Guid broadcasterId,
            string limitKey,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<ResourceUsageDto>>> GetUsageReportAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
