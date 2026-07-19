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
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
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
using NomNomzBot.Domain.Marketplace.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.CustomEvents;
using NomNomzBot.Infrastructure.Marketplace;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Marketplace;

/// <summary>
/// Proves the local bundle loop end to end (marketplace.md §6): export writes a complete, secret-free ZIP
/// with dependency edges; inspect surfaces the D4 capability summary; import recreates the entities with
/// fresh ids under the conflict policy, disables custom code, records the <see cref="InstalledBundle"/>
/// ledger row, and publishes <see cref="BundleInstalledEvent"/>; uninstall removes exactly what the import
/// created; a malformed or unknown-schema ZIP installs nothing.
/// </summary>
public sealed class BundleServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000b001");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000b002");
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-00000000b0aa");

    private const string RunCodeGraph = """
        {"nodes":[{"id":"n1","type":"run_code","config":{"scriptRef":"my-script"}},{"id":"n2","type":"send_message","config":{"text":"done"}}]}
        """;

    private sealed record Harness(
        MarketplaceTestDbContext Db,
        BundleExportService Export,
        BundleImportService Import,
        CommandService Commands,
        PipelineService Pipelines,
        CustomDataSourceService DataSources,
        RecordingEventBus Bus,
        ITokenProtector Protector
    );

    private static Harness Build()
    {
        MarketplaceTestDbContext db = MarketplaceTestDbContext.New();
        RecordingEventBus bus = new();
        ITokenProtector protector = Substitute.For<ITokenProtector>();
        protector
            .ProtectAsync(
                Arg.Any<string>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("SEALED-ENVELOPE");

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
            protector,
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
        return new Harness(db, export, import, commands, pipelines, dataSources, bus, protector);
    }

    private static ExportRequest Request(params ExportItemRef[] items) =>
        new(items, new BundleMetadata("Starter Pack", "1.0.0", "stoney", "MIT", "test bundle"));

    /// <summary>Seeds a pipeline (with the given graph) + a command executing it; returns their ids.</summary>
    private static async Task<(Guid PipelineId, Guid CommandId)> SeedCommandWithPipelineAsync(
        Harness h,
        string graph = RunCodeGraph,
        string commandName = "hello"
    )
    {
        PipelineDto pipeline = (
            await h.Pipelines.CreateAsync(
                Channel.ToString(),
                new CreatePipelineDto
                {
                    Name = "Greeting Flow",
                    Description = "greets",
                    TriggerKind = "command",
                    GraphJsonCache =
                        System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                            graph
                        ),
                }
            )
        ).Value;

        CommandDto command = (
            await h.Commands.CreateAsync(
                Channel.ToString(),
                new CreateCommandDto
                {
                    Name = commandName,
                    Tier = "pipeline",
                    PipelineId = pipeline.Id,
                    TemplateResponse = null,
                    CooldownSeconds = 5,
                }
            )
        ).Value;

        return (pipeline.Id, command.Id);
    }

    private static async Task<MemoryStream> ExportCommandBundleAsync(Harness h, Guid commandId)
    {
        Result<System.IO.Stream> zip = await h.Export.ExportAsync(
            Channel,
            Request(new ExportItemRef(BundleFormat.CommandType, commandId))
        );
        zip.IsSuccess.Should().BeTrue(zip.ErrorMessage);
        MemoryStream buffer = new();
        await zip.Value.CopyToAsync(buffer);
        buffer.Position = 0;
        return buffer;
    }

    private static Dictionary<string, string> ReadEntries(MemoryStream zip)
    {
        zip.Position = 0;
        using ZipArchive archive = new(zip, ZipArchiveMode.Read, leaveOpen: true);
        Dictionary<string, string> entries = [];
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            using StreamReader reader = new(entry.Open());
            entries[entry.FullName] = reader.ReadToEnd();
        }
        zip.Position = 0;
        return entries;
    }

    // ── Export ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_pulls_the_commands_pipeline_in_and_writes_the_dependency_edge()
    {
        Harness h = Build();
        (Guid pipelineId, Guid commandId) = await SeedCommandWithPipelineAsync(h);
        _ = pipelineId;

        MemoryStream zip = await ExportCommandBundleAsync(h, commandId);
        Dictionary<string, string> entries = ReadEntries(zip);

        entries.Keys.Should().Contain("manifest.json");
        entries.Keys.Should().Contain("commands/hello.json");
        entries.Keys.Should().Contain("pipelines/greeting-flow.json");

        BundleManifest manifest = BundleConventions.Deserialize<BundleManifest>(
            entries["manifest.json"]
        )!;
        manifest.Metadata.Name.Should().Be("Starter Pack");
        manifest.Items.Should().HaveCount(2);
        manifest
            .Items.Single(i => i.Type == BundleFormat.CommandType)
            .Dependencies.Should()
            .ContainSingle()
            .Which.Should()
            .Be("pipeline:Greeting Flow");

        CommandExport command = BundleConventions.Deserialize<CommandExport>(
            entries["commands/hello.json"]
        )!;
        command.Name.Should().Be("hello");
        command.PipelineName.Should().Be("Greeting Flow");
        command.SchemaVersion.Should().Be(BundleFormat.CurrentSchemaVersion);

        PipelineExport pipeline = BundleConventions.Deserialize<PipelineExport>(
            entries["pipelines/greeting-flow.json"]
        )!;
        pipeline.GraphJson.Should().Contain("run_code");
    }

    [Fact]
    public async Task Export_never_carries_a_data_sources_secret_in_any_form()
    {
        Harness h = Build();
        CustomDataSourceDto source = (
            await h.DataSources.CreateAsync(
                Channel,
                Actor,
                new UpsertCustomDataSourceRequest(
                    "heartrate",
                    "Heart Rate",
                    "poll",
                    "pulsoid",
                    "https://api.pulsoid.net/v1/hr",
                    AuthSecret: "hunter2-plaintext-token",
                    new Dictionary<string, string> { ["bpm"] = "$.data.heartRate" },
                    PollIntervalSeconds: 10,
                    IsEnabled: true
                )
            )
        ).Value;

        // The entity really carries a sealed secret — the export must not.
        (await h.Db.CustomDataSources.SingleAsync())
            .AuthSecretCipher.Should()
            .Be("SEALED-ENVELOPE");

        Result<System.IO.Stream> zip = await h.Export.ExportAsync(
            Channel,
            Request(new ExportItemRef(BundleFormat.CustomDataSourceType, source.Id))
        );
        zip.IsSuccess.Should().BeTrue(zip.ErrorMessage);
        MemoryStream buffer = new();
        await zip.Value.CopyToAsync(buffer);

        string allText = string.Join("\n", ReadEntries(buffer).Values);
        allText.Should().NotContain("hunter2-plaintext-token");
        allText.Should().NotContain("SEALED-ENVELOPE");
        allText.Should().NotContainEquivalentOf("authSecret");

        CustomDataSourceExport export = BundleConventions.Deserialize<CustomDataSourceExport>(
            ReadEntries(buffer)["custom-data-sources/heartrate.json"]
        )!;
        export.EndpointUrl.Should().Be("https://api.pulsoid.net/v1/hr");
        export.FieldMap.Should().ContainKey("bpm");
    }

    // ── Inspect ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inspect_reports_the_items_and_flags_custom_code_as_a_capability()
    {
        Harness h = Build();
        (_, Guid commandId) = await SeedCommandWithPipelineAsync(h);
        MemoryStream zip = await ExportCommandBundleAsync(h, commandId);

        Result<BundleInspection> inspection = await h.Import.InspectAsync(Channel, zip);

        inspection.IsSuccess.Should().BeTrue(inspection.ErrorMessage);
        inspection.Value.Issues.Should().BeEmpty();
        inspection.Value.Manifest.Items.Should().HaveCount(2);
        inspection.Value.Capabilities.Should().Contain("executes custom code");
        inspection.Value.Capabilities.Should().Contain("sends chat messages");
    }

    [Fact]
    public async Task Inspect_rejects_a_malformed_zip_outright()
    {
        Harness h = Build();
        using MemoryStream garbage = new(Encoding.UTF8.GetBytes("this is not a zip archive"));

        Result<BundleInspection> inspection = await h.Import.InspectAsync(Channel, garbage);

        inspection.IsFailure.Should().BeTrue();
        inspection.ErrorCode.Should().Be("BUNDLE_INVALID");
    }

    [Fact]
    public async Task Inspect_surfaces_an_unknown_schema_version_as_an_issue()
    {
        Harness h = Build();
        using MemoryStream zip = BuildZip(
            (
                "manifest.json",
                """{"schemaVersion":99,"metadata":{"name":"Future Pack","version":"9.9.9"},"items":[]}"""
            )
        );

        Result<BundleInspection> inspection = await h.Import.InspectAsync(Channel, zip);

        inspection.IsSuccess.Should().BeTrue(inspection.ErrorMessage);
        inspection.Value.Issues.Should().Contain(i => i.Contains("schemaVersion 99"));
    }

    // ── Import ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_recreates_the_entities_with_fresh_ids_records_the_ledger_and_publishes()
    {
        Harness source = Build();
        (Guid originalPipelineId, Guid originalCommandId) = await SeedCommandWithPipelineAsync(
            source
        );
        MemoryStream zip = await ExportCommandBundleAsync(source, originalCommandId);

        Harness target = Build();
        Result<InstalledBundleDto> installed = await target.Import.ImportAsync(
            OtherChannel,
            Actor,
            zip,
            ImportConflictPolicy.Rename
        );

        installed.IsSuccess.Should().BeTrue(installed.ErrorMessage);
        installed.Value.Name.Should().Be("Starter Pack");
        installed.Value.Source.Should().Be("local");
        installed.Value.Version.Should().Be("1.0.0");

        PipelineEntity pipeline = await target.Db.Pipelines.SingleAsync();
        pipeline.BroadcasterId.Should().Be(OtherChannel);
        pipeline.Name.Should().Be("Greeting Flow");
        pipeline.Id.Should().NotBe(originalPipelineId);

        Command command = await target.Db.Commands.SingleAsync();
        command.BroadcasterId.Should().Be(OtherChannel);
        command.Name.Should().Be("hello");
        command.Id.Should().NotBe(originalCommandId);
        command.PipelineId.Should().Be(pipeline.Id);

        InstalledBundle row = await target.Db.InstalledBundles.SingleAsync();
        row.BroadcasterId.Should().Be(OtherChannel);
        row.InstalledByUserId.Should().Be(Actor);
        row.MarketplaceItemId.Should().BeNull();
        row.InstalledEntityIdsJson.Should().Contain(pipeline.Id.ToString());
        row.InstalledEntityIdsJson.Should().Contain(command.Id.ToString());

        target
            .Bus.Published.OfType<BundleInstalledEvent>()
            .Should()
            .ContainSingle(e =>
                e.BroadcasterId == OtherChannel
                && e.InstalledBundleId == row.Id
                && e.Source == "local"
                && e.Capabilities.Contains("executes custom code")
            );
    }

    [Fact]
    public async Task Import_lands_a_run_code_pipeline_disabled()
    {
        Harness source = Build();
        (_, Guid commandId) = await SeedCommandWithPipelineAsync(source);

        // The exported pipeline is enabled — D4 must still force the run_code import off.
        (await source.Db.Pipelines.SingleAsync())
            .IsEnabled.Should()
            .BeTrue();
        MemoryStream zip = await ExportCommandBundleAsync(source, commandId);

        Harness target = Build();
        Result<InstalledBundleDto> installed = await target.Import.ImportAsync(
            OtherChannel,
            Actor,
            zip,
            ImportConflictPolicy.Rename
        );

        installed.IsSuccess.Should().BeTrue(installed.ErrorMessage);
        (await target.Db.Pipelines.SingleAsync()).IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Skip_leaves_the_existing_command_untouched()
    {
        Harness source = Build();
        CommandDto bundled = (
            await source.Commands.CreateAsync(
                Channel.ToString(),
                new CreateCommandDto { Name = "hello", TemplateResponse = "bundled response" }
            )
        ).Value;
        MemoryStream zip = await ExportCommandBundleAsync(source, bundled.Id);

        Harness target = Build();
        CommandDto existing = (
            await target.Commands.CreateAsync(
                OtherChannel.ToString(),
                new CreateCommandDto { Name = "hello", TemplateResponse = "original response" }
            )
        ).Value;

        Result<InstalledBundleDto> installed = await target.Import.ImportAsync(
            OtherChannel,
            Actor,
            zip,
            ImportConflictPolicy.Skip
        );

        installed.IsSuccess.Should().BeTrue(installed.ErrorMessage);
        Command command = await target.Db.Commands.SingleAsync();
        command.Id.Should().Be(existing.Id);
        command.TemplateResponse.Should().Be("original response");
        // Nothing was installed for the skipped item, so uninstall has nothing to remove.
        (await target.Db.InstalledBundles.SingleAsync())
            .InstalledEntityIdsJson.Should()
            .Be("{}");
    }

    [Fact]
    public async Task Overwrite_replaces_the_existing_command_with_the_bundled_one()
    {
        Harness source = Build();
        CommandDto bundled = (
            await source.Commands.CreateAsync(
                Channel.ToString(),
                new CreateCommandDto { Name = "hello", TemplateResponse = "bundled response" }
            )
        ).Value;
        MemoryStream zip = await ExportCommandBundleAsync(source, bundled.Id);

        Harness target = Build();
        CommandDto existing = (
            await target.Commands.CreateAsync(
                OtherChannel.ToString(),
                new CreateCommandDto { Name = "hello", TemplateResponse = "original response" }
            )
        ).Value;

        Result<InstalledBundleDto> installed = await target.Import.ImportAsync(
            OtherChannel,
            Actor,
            zip,
            ImportConflictPolicy.Overwrite
        );

        installed.IsSuccess.Should().BeTrue(installed.ErrorMessage);
        Command command = await target.Db.Commands.SingleAsync();
        command.Id.Should().NotBe(existing.Id);
        command.TemplateResponse.Should().Be("bundled response");
    }

    [Fact]
    public async Task Rename_installs_the_conflicting_command_under_a_suffixed_name()
    {
        Harness source = Build();
        CommandDto bundled = (
            await source.Commands.CreateAsync(
                Channel.ToString(),
                new CreateCommandDto { Name = "hello", TemplateResponse = "bundled response" }
            )
        ).Value;
        MemoryStream zip = await ExportCommandBundleAsync(source, bundled.Id);

        Harness target = Build();
        await target.Commands.CreateAsync(
            OtherChannel.ToString(),
            new CreateCommandDto { Name = "hello", TemplateResponse = "original response" }
        );

        Result<InstalledBundleDto> installed = await target.Import.ImportAsync(
            OtherChannel,
            Actor,
            zip,
            ImportConflictPolicy.Rename
        );

        installed.IsSuccess.Should().BeTrue(installed.ErrorMessage);
        List<Command> commands = await target.Db.Commands.OrderBy(c => c.Name).ToListAsync();
        commands.Should().HaveCount(2);
        commands.Select(c => c.Name).Should().BeEquivalentTo("hello", "hello-bundle");
        commands.Single(c => c.Name == "hello").TemplateResponse.Should().Be("original response");
        commands
            .Single(c => c.Name == "hello-bundle")
            .TemplateResponse.Should()
            .Be("bundled response");
    }

    // ── Uninstall ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Uninstall_removes_exactly_the_installed_entities_and_the_ledger_row()
    {
        Harness source = Build();
        (_, Guid commandId) = await SeedCommandWithPipelineAsync(source);
        MemoryStream zip = await ExportCommandBundleAsync(source, commandId);

        Harness target = Build();
        // An unrelated, pre-existing command must survive the uninstall untouched.
        CommandDto keeper = (
            await target.Commands.CreateAsync(
                OtherChannel.ToString(),
                new CreateCommandDto { Name = "keep-me", TemplateResponse = "stays" }
            )
        ).Value;

        InstalledBundleDto installed = (
            await target.Import.ImportAsync(OtherChannel, Actor, zip, ImportConflictPolicy.Rename)
        ).Value;

        Result uninstalled = await target.Import.UninstallAsync(OtherChannel, installed.Id, Actor);

        uninstalled.IsSuccess.Should().BeTrue(uninstalled.ErrorMessage);
        (await target.Db.Pipelines.CountAsync()).Should().Be(0);
        Command survivor = await target.Db.Commands.SingleAsync();
        survivor.Id.Should().Be(keeper.Id);
        (await target.Db.InstalledBundles.CountAsync()).Should().Be(0);

        Result<IReadOnlyList<InstalledBundleDto>> list = await target.Import.ListInstalledAsync(
            OtherChannel
        );
        list.Value.Should().BeEmpty();
    }

    // ── Invalid bundles create nothing ──────────────────────────────────────────

    [Fact]
    public async Task Import_of_a_malformed_zip_creates_nothing()
    {
        Harness h = Build();
        using MemoryStream garbage = new(Encoding.UTF8.GetBytes("still not a zip"));

        Result<InstalledBundleDto> installed = await h.Import.ImportAsync(
            Channel,
            Actor,
            garbage,
            ImportConflictPolicy.Rename
        );

        installed.IsFailure.Should().BeTrue();
        (await h.Db.Commands.CountAsync()).Should().Be(0);
        (await h.Db.Pipelines.CountAsync()).Should().Be(0);
        (await h.Db.InstalledBundles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Import_of_an_unknown_schema_version_creates_nothing()
    {
        Harness h = Build();
        using MemoryStream zip = BuildZip(
            (
                "manifest.json",
                """{"schemaVersion":99,"metadata":{"name":"Future Pack","version":"9.9.9"},"items":[]}"""
            )
        );

        Result<InstalledBundleDto> installed = await h.Import.ImportAsync(
            Channel,
            Actor,
            zip,
            ImportConflictPolicy.Rename
        );

        installed.IsFailure.Should().BeTrue();
        installed.ErrorCode.Should().Be("BUNDLE_INVALID");
        (await h.Db.InstalledBundles.CountAsync()).Should().Be(0);
    }

    private static MemoryStream BuildZip(params (string Path, string Content)[] entries)
    {
        MemoryStream buffer = new();
        using (ZipArchive archive = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, string content) in entries)
            {
                using StreamWriter writer = new(archive.CreateEntry(path).Open());
                writer.Write(content);
            }
        }
        buffer.Position = 0;
        return buffer;
    }
}
