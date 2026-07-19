// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Marketplace;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Application.Marketplace.Services;
using NomNomzBot.Application.PickLists.Services;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Marketplace.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.CustomEvents;
using NomNomzBot.Infrastructure.Marketplace;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Marketplace;

/// <summary>
/// Proves the hosted-marketplace flows over the ONE local import path (marketplace.md §6): a marketplace
/// install records <c>Source=marketplace</c> + the item id, and RE-installing the same item updates the
/// existing install (same ledger row, previous entities replaced, never a duplicate); an invalid ZIP is
/// refused locally before any publish upload; the per-channel install bucket denies the 11th install in the
/// window with the Retry-After seconds in the failure detail.
/// </summary>
public sealed class MarketplaceServiceTests
{
    private static readonly Guid SourceChannel = Guid.Parse("0192a000-0000-7000-8000-00000000d001");
    private static readonly Guid InstallChannel = Guid.Parse(
        "0192a000-0000-7000-8000-00000000d002"
    );
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-00000000d0aa");

    private sealed record Harness(
        MarketplaceTestDbContext Db,
        BundleImportService Import,
        BundleExportService Export,
        CommandService Commands,
        PipelineService Pipelines
    );

    private static Harness Build()
    {
        MarketplaceTestDbContext db = MarketplaceTestDbContext.New();
        RecordingEventBus bus = new();
        CommandService commands = new(
            db,
            Substitute.For<IPipelineEngine>(),
            Substitute.For<IChannelRegistry>(),
            bus,
            Billing.TestTiers.Unlimited()
        );
        PipelineService pipelines = new(db, bus);
        CustomDataSourceService dataSources = new(
            db,
            Substitute.For<ITokenProtector>(),
            Substitute.For<ICustomDataIngestService>(),
            []
        );
        BundleExportService export = new(
            db,
            Substitute.For<ISoundClipStore>(),
            Substitute.For<NomNomzBot.Application.Assets.Services.IChannelAssetStore>()
        );
        BundleImportService import = new(
            db,
            commands,
            pipelines,
            Substitute.For<IWidgetService>(),
            Substitute.For<ISoundClipService>(),
            Substitute.For<NomNomzBot.Application.Assets.Services.IChannelAssetService>(),
            dataSources,
            Substitute.For<IEventResponseService>(),
            Substitute.For<IRewardService>(),
            Substitute.For<ITimerManagementService>(),
            Substitute.For<IChatTriggerService>(),
            Substitute.For<IPickListService>(),
            Substitute.For<ICodeScriptService>(),
            Substitute.For<ICurrentTenantService>(),
            bus
        );
        return new Harness(db, import, export, commands, pipelines);
    }

    /// <summary>Seeds a command + pipeline on the source channel and exports them as real bundle bytes.</summary>
    private static async Task<byte[]> ExportedBundleAsync(Harness h, string version = "1.0.0")
    {
        PipelineDto pipeline = (
            await h.Pipelines.CreateAsync(
                SourceChannel.ToString(),
                new CreatePipelineDto
                {
                    Name = "Greeting Flow",
                    Description = "greets",
                    TriggerKind = "command",
                    GraphJsonCache =
                        System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                            """{"nodes":[{"id":"n1","type":"send_message","config":{"text":"hi"}}]}"""
                        ),
                }
            )
        ).Value;
        CommandDto command = (
            await h.Commands.CreateAsync(
                SourceChannel.ToString(),
                new CreateCommandDto
                {
                    Name = "hello",
                    Tier = "pipeline",
                    PipelineId = pipeline.Id,
                    TemplateResponse = null,
                    CooldownSeconds = 5,
                }
            )
        ).Value;

        Result<System.IO.Stream> zip = await h.Export.ExportAsync(
            SourceChannel,
            new ExportRequest(
                [new ExportItemRef(BundleFormat.CommandType, command.Id)],
                new BundleMetadata("Starter Pack", version, "stoney", "MIT", "test bundle")
            )
        );
        zip.IsSuccess.Should().BeTrue(zip.ErrorMessage);
        using MemoryStream buffer = new();
        await zip.Value.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static IMarketplaceClient ClientServing(byte[] zipBytes)
    {
        IMarketplaceClient client = Substitute.For<IMarketplaceClient>();
        client
            .DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Result.Success<System.IO.Stream>(new MemoryStream(zipBytes)));
        return client;
    }

    // ── Install ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Marketplace_install_records_the_marketplace_identity()
    {
        Harness h = Build();
        byte[] zipBytes = await ExportedBundleAsync(h);
        MarketplaceService service = new(
            ClientServing(zipBytes),
            h.Import,
            new CountingRateLimiterStore()
        );

        Result<InstalledBundleDto> result = await service.InstallAsync(
            InstallChannel,
            Actor,
            "itm_starter",
            ImportConflictPolicy.Rename
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Source.Should().Be("marketplace");
        result.Value.MarketplaceItemId.Should().Be("itm_starter");

        InstalledBundle row = (
            await h.Db.InstalledBundles.Where(b => b.BroadcasterId == InstallChannel).ToListAsync()
        )
            .Should()
            .ContainSingle()
            .Subject;
        row.Source.Should().Be("marketplace");
        row.MarketplaceItemId.Should().Be("itm_starter");

        // The bundle's content actually landed on the installing channel.
        List<Command> commands = await h
            .Db.Commands.Where(c => c.BroadcasterId == InstallChannel && c.DeletedAt == null)
            .ToListAsync();
        commands.Should().ContainSingle().Which.Name.Should().Be("hello");
    }

    [Fact]
    public async Task Reinstalling_the_same_item_updates_the_install_instead_of_duplicating()
    {
        Harness h = Build();
        byte[] v1 = await ExportedBundleAsync(h, version: "1.0.0");
        MarketplaceService service = new(
            ClientServing(v1),
            h.Import,
            new CountingRateLimiterStore()
        );

        Guid firstRowId = (
            await service.InstallAsync(
                InstallChannel,
                Actor,
                "itm_starter",
                ImportConflictPolicy.Rename
            )
        )
            .Value
            .Id;

        // Re-install the same marketplace item (a newer build of the same bundle).
        Result<InstalledBundleDto> again = await service.InstallAsync(
            InstallChannel,
            Actor,
            "itm_starter",
            ImportConflictPolicy.Rename
        );

        again.IsSuccess.Should().BeTrue(again.ErrorMessage);
        again.Value.Id.Should().Be(firstRowId, "re-install updates the SAME ledger row");

        List<InstalledBundle> rows = await h
            .Db.InstalledBundles.Where(b => b.BroadcasterId == InstallChannel)
            .ToListAsync();
        rows.Should()
            .ContainSingle("the unique (channel, source, item) key never sees a duplicate");

        // The previous version's entities gave way — exactly one live copy, no "-bundle" rename pile-up.
        List<Command> commands = await h
            .Db.Commands.Where(c => c.BroadcasterId == InstallChannel && c.DeletedAt == null)
            .ToListAsync();
        commands.Should().ContainSingle().Which.Name.Should().Be("hello");
    }

    // ── Publish ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_refuses_an_invalid_zip_locally_before_uploading()
    {
        Harness h = Build();
        IMarketplaceClient client = Substitute.For<IMarketplaceClient>();
        MarketplaceService service = new(client, h.Import, new CountingRateLimiterStore());

        using MemoryStream garbage = new(Encoding.UTF8.GetBytes("not a zip at all"));
        Result<PublishSubmissionDto> result = await service.PublishAsync(
            InstallChannel,
            garbage,
            new PublishMetadata("Starter Pack", "1.0.0", null, null)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("BUNDLE_INVALID");
        await client
            .DidNotReceive()
            .PublishAsync(
                Arg.Any<Guid>(),
                Arg.Any<System.IO.Stream>(),
                Arg.Any<PublishMetadata>(),
                Arg.Any<CancellationToken>()
            );
    }

    // ── Rate limiting ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Eleventh_install_in_the_window_is_denied_with_retry_after()
    {
        Harness h = Build();
        byte[] zipBytes = await ExportedBundleAsync(h);
        CountingRateLimiterStore limiter = new();
        MarketplaceService service = new(ClientServing(zipBytes), h.Import, limiter);

        for (int attempt = 1; attempt <= 10; attempt++)
        {
            Result<InstalledBundleDto> allowed = await service.InstallAsync(
                InstallChannel,
                Actor,
                "itm_starter",
                ImportConflictPolicy.Rename
            );
            allowed.IsSuccess.Should().BeTrue($"install {attempt} is within the 10/hour budget");
        }

        Result<InstalledBundleDto> eleventh = await service.InstallAsync(
            InstallChannel,
            Actor,
            "itm_starter",
            ImportConflictPolicy.Rename
        );

        eleventh.IsFailure.Should().BeTrue();
        eleventh.ErrorCode.Should().Be("RATE_LIMITED");
        eleventh.ErrorDetail.Should().Be("1800", "the Retry-After seconds ride in the detail");
        limiter.SeenKeys.Should().OnlyContain(k => k == $"marketplace:{InstallChannel}:install");
    }

    /// <summary>
    /// A window-counter fake mirroring <c>IRateLimiterPartitionStore</c> semantics: permits until the
    /// per-key count exceeds the limit, then denies with a fixed 30-minute Retry-After.
    /// </summary>
    private sealed class CountingRateLimiterStore : IRateLimiterPartitionStore
    {
        private readonly Dictionary<string, int> _counts = [];

        public List<string> SeenKeys { get; } = [];

        public Task<RateLimitLease> AcquireAsync(
            string partitionKey,
            int permitLimit,
            TimeSpan window,
            CancellationToken cancellationToken = default
        )
        {
            SeenKeys.Add(partitionKey);
            int count = _counts.GetValueOrDefault(partitionKey) + 1;
            _counts[partitionKey] = count;
            return Task.FromResult(
                count <= permitLimit
                    ? new RateLimitLease(true, permitLimit - count, TimeSpan.Zero)
                    : new RateLimitLease(false, 0, TimeSpan.FromMinutes(30))
            );
        }
    }
}
