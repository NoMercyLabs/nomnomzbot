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
using NomNomzBot.Application.Assets.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Assets.Entities;
using NomNomzBot.Infrastructure.Assets;
using NomNomzBot.Infrastructure.Tests.Marketplace;

namespace NomNomzBot.Infrastructure.Tests.Assets;

/// <summary>
/// Behavior of the channel asset library: uploads are content-SNIFFED (spoofed extensions/types lose),
/// both size caps enforce, upload replaces by name keeping ONE live row at a stable URL, delete removes
/// both the row and the blob, serving resolves live assets only, and tenants never see each other's assets.
/// </summary>
public sealed class ChannelAssetServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-00000000a001");
    private static readonly Guid OtherChannel = Guid.Parse("0192b000-0000-7000-8000-00000000a002");
    private static readonly Guid Actor = Guid.Parse("0192b000-0000-7000-8000-00000000a0aa");

    private static (
        ChannelAssetService Service,
        MarketplaceTestDbContext Db,
        FakeAssetStore Store
    ) Build()
    {
        MarketplaceTestDbContext db = MarketplaceTestDbContext.New();
        FakeAssetStore store = new();
        return (new ChannelAssetService(db, store), db, store);
    }

    // ── Payloads with real magic bytes ──────────────────────────────────────────

    internal static byte[] PngBytes(int totalLength = 64)
    {
        byte[] bytes = new byte[totalLength];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        for (int i = 8; i < totalLength; i++)
            bytes[i] = (byte)(i % 251);
        return bytes;
    }

    internal static byte[] Mp3Bytes()
    {
        byte[] bytes = new byte[64];
        bytes[0] = 0x49; // 'I'
        bytes[1] = 0x44; // 'D'
        bytes[2] = 0x33; // '3'
        return bytes;
    }

    internal static byte[] SvgBytes() =>
        System.Text.Encoding.UTF8.GetBytes(
            """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>"""
        );

    private static UploadChannelAssetRequest Request(
        string name,
        byte[] payload,
        string fileName
    ) => new(name, name, fileName, new MemoryStream(payload));

    // ── Upload: sniffing, kind, shape ───────────────────────────────────────────

    [Fact]
    public async Task Upload_persists_a_sniffed_image_row_and_the_bytes_serve_back_identically()
    {
        (ChannelAssetService service, MarketplaceTestDbContext db, FakeAssetStore store) = Build();
        byte[] payload = PngBytes();

        Result<ChannelAssetDto> uploaded = await service.UploadAsync(
            Channel,
            Actor,
            Request("boot-screen", payload, "boot-screen.png")
        );

        uploaded.IsSuccess.Should().BeTrue(uploaded.ErrorMessage);
        uploaded.Value.Kind.Should().Be("image");
        uploaded.Value.MimeType.Should().Be("image/png");
        uploaded.Value.SizeBytes.Should().Be(payload.Length);
        uploaded.Value.Url.Should().StartWith($"/api/v1/assets/file/{Channel}/boot-screen?v=");

        ChannelAsset row = await db.ChannelAssets.SingleAsync();
        row.BroadcasterId.Should().Be(Channel);
        row.Name.Should().Be("boot-screen");
        row.Kind.Should().Be("image");
        row.MimeType.Should().Be("image/png");
        row.SizeBytes.Should().Be(payload.Length);
        row.CreatedByUserId.Should().Be(Actor);

        Result<ChannelAssetContent> served = await service.OpenForServingAsync(
            Channel,
            "boot-screen"
        );
        served.IsSuccess.Should().BeTrue(served.ErrorMessage);
        served.Value.MimeType.Should().Be("image/png");
        using MemoryStream roundTrip = new();
        await served.Value.Content.CopyToAsync(roundTrip);
        roundTrip.ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task Upload_derives_kind_audio_from_sniffed_audio_content()
    {
        (ChannelAssetService service, MarketplaceTestDbContext db, FakeAssetStore _) = Build();

        Result<ChannelAssetDto> uploaded = await service.UploadAsync(
            Channel,
            Actor,
            Request("chime", Mp3Bytes(), "chime.mp3")
        );

        uploaded.IsSuccess.Should().BeTrue(uploaded.ErrorMessage);
        uploaded.Value.Kind.Should().Be("audio");
        uploaded.Value.MimeType.Should().Be("audio/mpeg");
        (await db.ChannelAssets.SingleAsync()).MimeType.Should().Be("audio/mpeg");
    }

    [Fact]
    public async Task Upload_sniffs_svg_content_as_svg()
    {
        (ChannelAssetService service, MarketplaceTestDbContext _, FakeAssetStore _) = Build();

        Result<ChannelAssetDto> uploaded = await service.UploadAsync(
            Channel,
            Actor,
            Request("feather", SvgBytes(), "feather.svg")
        );

        uploaded.IsSuccess.Should().BeTrue(uploaded.ErrorMessage);
        uploaded.Value.Kind.Should().Be("image");
        uploaded.Value.MimeType.Should().Be("image/svg+xml");
    }

    [Fact]
    public async Task Spoofed_content_is_rejected_by_the_sniffer_regardless_of_file_name()
    {
        (ChannelAssetService service, MarketplaceTestDbContext db, FakeAssetStore store) = Build();
        byte[] notAnImage = System.Text.Encoding.UTF8.GetBytes(
            "hello world this is not an image at all"
        );

        Result<ChannelAssetDto> uploaded = await service.UploadAsync(
            Channel,
            Actor,
            Request("totally-a-png", notAnImage, "totally-a-png.png")
        );

        uploaded.IsFailure.Should().BeTrue();
        uploaded.ErrorCode.Should().Be("INVALID_FORMAT");
        (await db.ChannelAssets.CountAsync()).Should().Be(0);
        store.Blobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Mp3_bytes_named_png_store_as_audio_not_as_the_claimed_image()
    {
        (ChannelAssetService service, MarketplaceTestDbContext _, FakeAssetStore _) = Build();

        Result<ChannelAssetDto> uploaded = await service.UploadAsync(
            Channel,
            Actor,
            Request("sneaky", Mp3Bytes(), "sneaky.png")
        );

        // The sniff wins: the row carries the REAL type, so serving can never claim image/png.
        uploaded.IsSuccess.Should().BeTrue(uploaded.ErrorMessage);
        uploaded.Value.MimeType.Should().Be("audio/mpeg");
        uploaded.Value.Kind.Should().Be("audio");
    }

    [Fact]
    public async Task Invalid_name_slug_is_rejected()
    {
        (ChannelAssetService service, MarketplaceTestDbContext db, FakeAssetStore _) = Build();

        Result<ChannelAssetDto> uploaded = await service.UploadAsync(
            Channel,
            Actor,
            Request("../escape", PngBytes(), "escape.png")
        );

        uploaded.IsFailure.Should().BeTrue();
        uploaded.ErrorCode.Should().Be("INVALID_NAME");
        (await db.ChannelAssets.CountAsync()).Should().Be(0);
    }

    // ── Size caps ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_over_the_8mb_per_asset_cap_is_rejected()
    {
        (ChannelAssetService service, MarketplaceTestDbContext db, FakeAssetStore store) = Build();
        byte[] tooBig = PngBytes(8 * 1024 * 1024 + 1);

        Result<ChannelAssetDto> uploaded = await service.UploadAsync(
            Channel,
            Actor,
            Request("huge", tooBig, "huge.png")
        );

        uploaded.IsFailure.Should().BeTrue();
        uploaded.ErrorCode.Should().Be("SIZE_EXCEEDED");
        (await db.ChannelAssets.CountAsync()).Should().Be(0);
        store.Blobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_that_would_break_the_64mb_channel_budget_is_rejected()
    {
        (ChannelAssetService service, MarketplaceTestDbContext db, FakeAssetStore _) = Build();
        // Seed live rows summing to just under the budget — a 1 MB upload must push it over.
        for (int i = 0; i < 8; i++)
        {
            db.ChannelAssets.Add(
                new ChannelAsset
                {
                    Id = Guid.NewGuid(),
                    BroadcasterId = Channel,
                    Name = $"existing-{i}",
                    DisplayName = $"existing-{i}",
                    Kind = "image",
                    MimeType = "image/png",
                    StorageKey = $"k/{i}",
                    SizeBytes = 8 * 1024 * 1024 - 1024,
                    CreatedByUserId = Actor,
                }
            );
        }
        await db.SaveChangesAsync();

        Result<ChannelAssetDto> uploaded = await service.UploadAsync(
            Channel,
            Actor,
            Request("straw", PngBytes(1024 * 1024), "straw.png")
        );

        uploaded.IsFailure.Should().BeTrue();
        uploaded.ErrorCode.Should().Be("CHANNEL_BUDGET_EXCEEDED");
        (await db.ChannelAssets.CountAsync()).Should().Be(8);
    }

    [Fact]
    public async Task Channel_budget_ignores_the_row_being_replaced_and_other_channels()
    {
        (ChannelAssetService service, MarketplaceTestDbContext db, FakeAssetStore _) = Build();
        // 60 MB live on THIS channel under one name; another channel is irrelevant to the budget.
        db.ChannelAssets.Add(
            new ChannelAsset
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Channel,
                Name = "big",
                DisplayName = "big",
                Kind = "image",
                MimeType = "image/png",
                StorageKey = "k/big",
                SizeBytes = 60 * 1024 * 1024,
                CreatedByUserId = Actor,
            }
        );
        db.ChannelAssets.Add(
            new ChannelAsset
            {
                Id = Guid.NewGuid(),
                BroadcasterId = OtherChannel,
                Name = "other-big",
                DisplayName = "other-big",
                Kind = "image",
                MimeType = "image/png",
                StorageKey = "k/other",
                SizeBytes = 60 * 1024 * 1024,
                CreatedByUserId = Actor,
            }
        );
        await db.SaveChangesAsync();

        // Replacing "big" with 5 MB fits (its own 60 MB no longer counts against the budget).
        Result<ChannelAssetDto> replaced = await service.UploadAsync(
            Channel,
            Actor,
            Request("big", PngBytes(5 * 1024 * 1024), "big.png")
        );

        replaced.IsSuccess.Should().BeTrue(replaced.ErrorMessage);
        replaced.Value.SizeBytes.Should().Be(5 * 1024 * 1024);
    }

    // ── Replace by name ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Replacing_by_name_keeps_one_live_row_serves_the_new_bytes_and_drops_the_old_blob()
    {
        (ChannelAssetService service, MarketplaceTestDbContext db, FakeAssetStore store) = Build();
        byte[] first = PngBytes(200);
        byte[] second = Mp3Bytes();

        Result<ChannelAssetDto> created = await service.UploadAsync(
            Channel,
            Actor,
            Request("alert", first, "alert.png")
        );
        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        string firstKey = (await db.ChannelAssets.SingleAsync()).StorageKey;

        Result<ChannelAssetDto> replaced = await service.UploadAsync(
            Channel,
            Actor,
            Request("alert", second, "alert.mp3")
        );

        replaced.IsSuccess.Should().BeTrue(replaced.ErrorMessage);
        replaced.Value.Id.Should().Be(created.Value.Id); // same row, same identity
        replaced.Value.MimeType.Should().Be("audio/mpeg");
        replaced.Value.Kind.Should().Be("audio");

        ChannelAsset row = await db.ChannelAssets.SingleAsync(); // still exactly one live row
        row.StorageKey.Should().NotBe(firstKey);
        store.Blobs.Should().NotContainKey(firstKey); // the old blob is gone

        Result<ChannelAssetContent> served = await service.OpenForServingAsync(Channel, "alert");
        served.IsSuccess.Should().BeTrue(served.ErrorMessage);
        using MemoryStream roundTrip = new();
        await served.Value.Content.CopyToAsync(roundTrip);
        roundTrip.ToArray().Should().Equal(second);
    }

    // ── Delete ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_soft_deletes_the_row_removes_the_blob_and_stops_serving()
    {
        (ChannelAssetService service, MarketplaceTestDbContext db, FakeAssetStore store) = Build();
        Result<ChannelAssetDto> created = await service.UploadAsync(
            Channel,
            Actor,
            Request("gone-soon", PngBytes(), "gone.png")
        );
        string key = (await db.ChannelAssets.SingleAsync()).StorageKey;

        Result deleted = await service.DeleteAsync(Channel, created.Value.Id, Actor);

        deleted.IsSuccess.Should().BeTrue(deleted.ErrorMessage);
        (await db.ChannelAssets.CountAsync()).Should().Be(0); // filtered out (soft-deleted)
        (await db.ChannelAssets.IgnoreQueryFilters().SingleAsync()).DeletedAt.Should().NotBeNull();
        store.Blobs.Should().NotContainKey(key);

        Result<ChannelAssetContent> served = await service.OpenForServingAsync(
            Channel,
            "gone-soon"
        );
        served.IsFailure.Should().BeTrue();
        served.ErrorCode.Should().Be("NOT_FOUND");
    }

    // ── Tenant isolation ────────────────────────────────────────────────────────

    [Fact]
    public async Task Assets_are_invisible_to_other_channels_across_list_get_and_serving()
    {
        (ChannelAssetService service, MarketplaceTestDbContext _, FakeAssetStore _) = Build();
        Result<ChannelAssetDto> created = await service.UploadAsync(
            Channel,
            Actor,
            Request("mine", PngBytes(), "mine.png")
        );

        Result<PagedList<ChannelAssetDto>> otherList = await service.ListAsync(
            OtherChannel,
            new PaginationParams(1, 25)
        );
        otherList.Value.Items.Should().BeEmpty();

        Result<ChannelAssetDto> otherGet = await service.GetAsync(OtherChannel, created.Value.Id);
        otherGet.IsFailure.Should().BeTrue();

        // Serving is per-channel: the same name under another channel id resolves nothing.
        Result<ChannelAssetContent> otherServe = await service.OpenForServingAsync(
            OtherChannel,
            "mine"
        );
        otherServe.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task List_pages_and_counts_the_channel_scope_only()
    {
        (ChannelAssetService service, MarketplaceTestDbContext _, FakeAssetStore _) = Build();
        foreach (string name in (string[])["a-one", "b-two", "c-three"])
        {
            Result<ChannelAssetDto> uploaded = await service.UploadAsync(
                Channel,
                Actor,
                Request(name, PngBytes(), $"{name}.png")
            );
            uploaded.IsSuccess.Should().BeTrue(uploaded.ErrorMessage);
        }

        Result<PagedList<ChannelAssetDto>> page = await service.ListAsync(
            Channel,
            new PaginationParams(1, 2)
        );

        page.Value.Items.Should().HaveCount(2);
        page.Value.TotalCount.Should().Be(3);
        page.Value.Items.Select(i => i.Name).Should().Equal("a-one", "b-two");
    }
}

/// <summary>An in-memory <see cref="IChannelAssetStore"/> that records blobs by storage key.</summary>
internal sealed class FakeAssetStore : IChannelAssetStore
{
    public Dictionary<string, byte[]> Blobs { get; } = [];

    public async Task<Result<string>> PutAsync(
        Guid broadcasterId,
        string fileName,
        System.IO.Stream content,
        string mimeType,
        CancellationToken ct = default
    )
    {
        using MemoryStream ms = new();
        await content.CopyToAsync(ms, ct);
        string key = $"{broadcasterId:N}/{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        Blobs[key] = ms.ToArray();
        return Result<string>.Success(key);
    }

    public Task<Result<System.IO.Stream>> OpenAsync(
        string storageKey,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Blobs.TryGetValue(storageKey, out byte[]? bytes)
                ? Result<System.IO.Stream>.Success(new MemoryStream(bytes))
                : Result<System.IO.Stream>.Failure("Asset file not found.")
        );

    public Task<Result> DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        Blobs.Remove(storageKey);
        return Task.FromResult(Result.Success());
    }
}
