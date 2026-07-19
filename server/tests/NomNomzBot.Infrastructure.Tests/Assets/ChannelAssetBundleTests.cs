// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IO.Compression;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Assets.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Marketplace;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Application.Marketplace.Services;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Assets.Entities;
using NomNomzBot.Infrastructure.Assets;
using NomNomzBot.Infrastructure.Marketplace;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Marketplace;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Assets;

/// <summary>
/// The <c>asset</c> bundle item type end to end: export writes the allowlisted metadata plus the payload
/// as a sibling ZIP entry (never a storage key), inspect surfaces the capability, import re-uploads the
/// payload through the asset module into a DIFFERENT tenant (bytes equal, sniff re-runs), collisions follow
/// the conflict policy, and uninstall removes the asset row AND its bytes.
/// </summary>
public sealed class ChannelAssetBundleTests
{
    private static readonly Guid Channel = Guid.Parse("0192c000-0000-7000-8000-00000000b001");
    private static readonly Guid OtherChannel = Guid.Parse("0192c000-0000-7000-8000-00000000b002");
    private static readonly Guid Actor = Guid.Parse("0192c000-0000-7000-8000-00000000b0aa");

    private sealed record Harness(
        MarketplaceTestDbContext Db,
        Guid ActingChannel,
        ChannelAssetService Assets,
        FakeAssetStore Store,
        BundleExportService Export,
        BundleImportService Import
    );

    private static Harness Build(Guid actingChannel)
    {
        MarketplaceTestDbContext db = MarketplaceTestDbContext.New();
        FakeAssetStore store = new();
        ChannelAssetService assets = new(db, store);

        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(actingChannel);

        BundleExportService export = new(db, Substitute.For<ISoundClipStore>(), store);
        BundleImportService import = new(
            db,
            Substitute.For<NomNomzBot.Application.Commands.Services.ICommandService>(),
            Substitute.For<NomNomzBot.Application.Commands.Services.IPipelineService>(),
            Substitute.For<IWidgetService>(),
            Substitute.For<ISoundClipService>(),
            assets,
            Substitute.For<ICustomDataSourceService>(),
            Substitute.For<NomNomzBot.Application.Commands.Services.IEventResponseService>(),
            Substitute.For<NomNomzBot.Application.Rewards.Services.IRewardService>(),
            Substitute.For<NomNomzBot.Application.Commands.Services.ITimerManagementService>(),
            Substitute.For<NomNomzBot.Application.Commands.Services.IChatTriggerService>(),
            Substitute.For<NomNomzBot.Application.PickLists.Services.IPickListService>(),
            Substitute.For<NomNomzBot.Application.Contracts.CustomCode.ICodeScriptService>(),
            tenant,
            new RecordingEventBus()
        );
        return new Harness(db, actingChannel, assets, store, export, import);
    }

    private static async Task<Guid> SeedAssetAsync(Harness h, string name, byte[] payload)
    {
        Result<ChannelAssetDto> uploaded = await h.Assets.UploadAsync(
            h.ActingChannel,
            Actor,
            new UploadChannelAssetRequest(name, name, $"{name}.png", new MemoryStream(payload))
        );
        uploaded.IsSuccess.Should().BeTrue(uploaded.ErrorMessage);
        return uploaded.Value.Id;
    }

    private static async Task<MemoryStream> ExportAssetAsync(Harness h, Guid assetId)
    {
        Result<System.IO.Stream> zip = await h.Export.ExportAsync(
            h.ActingChannel,
            new ExportRequest(
                [new ExportItemRef(BundleFormat.AssetType, assetId)],
                new BundleMetadata("Asset Pack", "1.0.0", "stoney", "MIT", "media")
            )
        );
        zip.IsSuccess.Should().BeTrue(zip.ErrorMessage);
        MemoryStream buffer = new();
        await zip.Value.CopyToAsync(buffer);
        buffer.Position = 0;
        return buffer;
    }

    [Fact]
    public async Task Export_writes_metadata_and_the_payload_as_a_sibling_zip_entry()
    {
        Harness h = Build(Channel);
        byte[] payload = ChannelAssetServiceTests.PngBytes(300);
        Guid assetId = await SeedAssetAsync(h, "boot-screen", payload);

        MemoryStream zip = await ExportAssetAsync(h, assetId);

        using ZipArchive archive = new(zip, ZipArchiveMode.Read, leaveOpen: true);
        archive
            .Entries.Select(e => e.FullName)
            .Should()
            .BeEquivalentTo("manifest.json", "assets/boot-screen.json", "assets/boot-screen.png");

        using StreamReader reader = new(archive.GetEntry("assets/boot-screen.json")!.Open());
        AssetExport export = BundleConventions.Deserialize<AssetExport>(reader.ReadToEnd())!;
        export.Name.Should().Be("boot-screen");
        export.DisplayName.Should().Be("boot-screen");
        export.MimeType.Should().Be("image/png");
        export.PayloadPath.Should().Be("assets/boot-screen.png");

        using MemoryStream payloadEntry = new();
        await using (System.IO.Stream entry = archive.GetEntry("assets/boot-screen.png")!.Open())
        {
            await entry.CopyToAsync(payloadEntry);
        }
        payloadEntry.ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task Inspect_lists_the_asset_item_and_surfaces_the_capability()
    {
        Harness h = Build(Channel);
        Guid assetId = await SeedAssetAsync(h, "chime", ChannelAssetServiceTests.PngBytes());
        MemoryStream zip = await ExportAssetAsync(h, assetId);

        Result<BundleInspection> inspection = await h.Import.InspectAsync(Channel, zip);

        inspection.IsSuccess.Should().BeTrue(inspection.ErrorMessage);
        inspection.Value.Issues.Should().BeEmpty();
        inspection
            .Value.Manifest.Items.Should()
            .ContainSingle(i => i.Type == BundleFormat.AssetType && i.Name == "chime");
        inspection
            .Value.Capabilities.Should()
            .Contain("adds media assets (images/audio for overlays)");
    }

    [Fact]
    public async Task Round_trip_reuploads_the_payload_into_another_tenant_with_bytes_equal()
    {
        Harness source = Build(Channel);
        byte[] payload = ChannelAssetServiceTests.PngBytes(500);
        Guid assetId = await SeedAssetAsync(source, "boot-screen", payload);
        MemoryStream zip = await ExportAssetAsync(source, assetId);

        Harness target = Build(OtherChannel);
        Result<InstalledBundleDto> installed = await target.Import.ImportAsync(
            OtherChannel,
            Actor,
            zip,
            ImportConflictPolicy.Rename
        );
        installed.IsSuccess.Should().BeTrue(installed.ErrorMessage);

        ChannelAsset imported = await target.Db.ChannelAssets.SingleAsync();
        imported.BroadcasterId.Should().Be(OtherChannel);
        imported.Name.Should().Be("boot-screen");
        imported.MimeType.Should().Be("image/png"); // re-sniffed on the importing instance
        imported.Kind.Should().Be("image");
        imported.Id.Should().NotBe(assetId);
        imported.SizeBytes.Should().Be(payload.Length);

        Result<ChannelAssetContent> served = await target.Assets.OpenForServingAsync(
            OtherChannel,
            "boot-screen"
        );
        served.IsSuccess.Should().BeTrue(served.ErrorMessage);
        using MemoryStream roundTrip = new();
        await served.Value.Content.CopyToAsync(roundTrip);
        roundTrip.ToArray().Should().Equal(payload);

        // The ledger row records the asset so uninstall can remove it exactly.
        Domain.Marketplace.Entities.InstalledBundle row =
            await target.Db.InstalledBundles.SingleAsync();
        row.InstalledEntityIdsJson.Should().Contain(imported.Id.ToString());
    }

    [Fact]
    public async Task Reimport_renames_on_collision_and_skip_leaves_the_existing_asset_alone()
    {
        Harness source = Build(Channel);
        Guid assetId = await SeedAssetAsync(source, "chime", ChannelAssetServiceTests.PngBytes());
        MemoryStream zip = await ExportAssetAsync(source, assetId);

        Harness target = Build(OtherChannel);
        (await target.Import.ImportAsync(OtherChannel, Actor, zip, ImportConflictPolicy.Rename))
            .IsSuccess.Should()
            .BeTrue();
        zip.Position = 0;
        (await target.Import.ImportAsync(OtherChannel, Actor, zip, ImportConflictPolicy.Rename))
            .IsSuccess.Should()
            .BeTrue();

        // The slug-typed asset name renames with a slug-safe suffix.
        (await target.Db.ChannelAssets.Select(a => a.Name).ToListAsync())
            .Should()
            .BeEquivalentTo("chime", "chime-bundle");

        zip.Position = 0;
        (await target.Import.ImportAsync(OtherChannel, Actor, zip, ImportConflictPolicy.Skip))
            .IsSuccess.Should()
            .BeTrue();
        (await target.Db.ChannelAssets.CountAsync()).Should().Be(2); // skip created nothing
    }

    [Fact]
    public async Task Uninstall_removes_the_asset_row_and_its_bytes()
    {
        Harness source = Build(Channel);
        Guid assetId = await SeedAssetAsync(source, "chime", ChannelAssetServiceTests.PngBytes());
        MemoryStream zip = await ExportAssetAsync(source, assetId);

        Harness target = Build(OtherChannel);
        InstalledBundleDto installed = (
            await target.Import.ImportAsync(OtherChannel, Actor, zip, ImportConflictPolicy.Rename)
        ).Value;
        target.Store.Blobs.Should().NotBeEmpty();

        Result uninstalled = await target.Import.UninstallAsync(OtherChannel, installed.Id, Actor);

        uninstalled.IsSuccess.Should().BeTrue(uninstalled.ErrorMessage);
        (await target.Db.ChannelAssets.CountAsync()).Should().Be(0);
        target.Store.Blobs.Should().BeEmpty(); // the blob is gone with the row
        (await target.Db.InstalledBundles.CountAsync()).Should().Be(0);

        Result<ChannelAssetContent> served = await target.Assets.OpenForServingAsync(
            OtherChannel,
            "chime"
        );
        served.IsFailure.Should().BeTrue();
    }
}
