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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Marketplace;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.Marketplace.Services;
using NomNomzBot.Application.PickLists.Dtos;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.PickLists.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomEvents;
using NomNomzBot.Infrastructure.Marketplace;
using NomNomzBot.Infrastructure.PickLists;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Marketplace;

/// <summary>
/// Proves the six parity bundle item types (event_response / reward / timer / chat_trigger / pick_list /
/// code_script) travel through the ONE generic bundle surface end to end: export writes the allowlisted
/// shape with `pipeline:&lt;name&gt;` edges (auto-pulling the bound pipeline), import recreates each entity
/// through its owning module service on a DIFFERENT tenant (re-linking pipelines BY NAME to fresh ids),
/// event responses UPSERT by event type while every named type renames on collision, code scripts always
/// land disabled with their declared capabilities intact, rewards land as local manageable definitions with
/// no Twitch id, a bad bundle fails typed before/with a full rollback, and uninstall removes every
/// installed entity.
/// </summary>
public sealed class BundleParityTypesTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000c001");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000c002");
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-00000000c0aa");

    private const string SendMessageGraph = """
        {"nodes":[{"id":"n1","type":"send_message","config":{"text":"hi"}}]}
        """;

    private sealed record Harness(
        MarketplaceTestDbContext Db,
        Guid ActingChannel,
        BundleExportService Export,
        BundleImportService Import,
        PipelineService Pipelines,
        EventResponseService EventResponses,
        RewardService Rewards,
        TimerManagementService Timers,
        ChatTriggerService ChatTriggers,
        PickListService PickLists,
        CodeScriptService CodeScripts,
        IScriptExecutor Executor,
        RecordingEventBus Bus
    );

    /// <summary>
    /// A harness acting AS <paramref name="actingChannel"/>: the tenant-ambient code-script module and the
    /// seeded <see cref="Channel"/> row (reward/pick-list creation verifies it) are bound to that channel.
    /// </summary>
    private static Harness Build(Guid actingChannel)
    {
        MarketplaceTestDbContext db = MarketplaceTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = actingChannel,
                OwnerUserId = Actor,
                Name = "tester",
                NameNormalized = "tester",
            }
        );
        db.SaveChanges();

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
        EventResponseService eventResponses = new(db, bus, Billing.TestTiers.Unlimited());
        RewardService rewards = new(
            db,
            Substitute.For<ITwitchChannelPointsApi>(),
            NullLogger<RewardService>.Instance
        );
        TimerManagementService timers = new(db, bus, Billing.TestTiers.Unlimited());
        ChatTriggerService chatTriggers = new(db, Substitute.For<IChannelRegistry>());
        PickListService pickLists = new(db, bus);

        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(actingChannel);
        IScriptExecutor executor = Substitute.For<IScriptExecutor>();
        executor
            .CompileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new ScriptCompilation(
                        "compiled-js",
                        "hash-1",
                        new List<string> { "chat.send", "http.fetch" }
                    )
                )
            );
        IWidgetDependencyAllowlist allowlist = Substitute.For<IWidgetDependencyAllowlist>();
        allowlist.IsAllowed(Arg.Any<string>()).Returns(true);
        CodeScriptService codeScripts = new(
            db,
            tenant,
            executor,
            bus,
            TimeProvider.System,
            allowlist
        );

        BundleExportService export = new(db, Substitute.For<ISoundClipStore>());
        BundleImportService import = new(
            db,
            commands,
            pipelines,
            Substitute.For<IWidgetService>(),
            Substitute.For<ISoundClipService>(),
            dataSources,
            eventResponses,
            rewards,
            timers,
            chatTriggers,
            pickLists,
            codeScripts,
            tenant,
            bus
        );
        return new Harness(
            db,
            actingChannel,
            export,
            import,
            pipelines,
            eventResponses,
            rewards,
            timers,
            chatTriggers,
            pickLists,
            codeScripts,
            executor,
            bus
        );
    }

    // ── Seeding: one of every parity type on the source channel, all bound to one pipeline ──

    private sealed record SeededParityContent(
        Guid PipelineId,
        Guid EventResponseId,
        Guid RewardId,
        Guid TimerId,
        Guid ChatTriggerId,
        Guid PickListId,
        Guid CodeScriptId
    );

    private static async Task<SeededParityContent> SeedAllAsync(Harness h)
    {
        string channelId = h.ActingChannel.ToString();

        PipelineDto pipeline = (
            await h.Pipelines.CreateAsync(
                channelId,
                new CreatePipelineDto
                {
                    Name = "Greeting Flow",
                    TriggerKind = "event",
                    GraphJsonCache =
                        System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                            SendMessageGraph
                        ),
                }
            )
        ).Value;

        EventResponseDto eventResponse = (
            await h.EventResponses.UpsertAsync(
                channelId,
                "channel.follow",
                new UpdateEventResponseDto
                {
                    IsEnabled = true,
                    ResponseType = "pipeline",
                    Message = "Thanks for the follow, {user}!",
                    PipelineId = pipeline.Id,
                    Metadata = new Dictionary<string, string> { ["widget"] = "alert-box" },
                }
            )
        ).Value;

        RewardDetail reward = (
            await h.Rewards.CreateAsync(
                channelId,
                new CreateRewardRequest
                {
                    Title = "Hydrate!",
                    Cost = 250,
                    Prompt = "Make the streamer drink water",
                    Response = "{user} made me drink!",
                    TimerDurationSeconds = 90,
                    PipelineId = pipeline.Id,
                }
            )
        ).Value;

        TimerDto timer = (
            await h.Timers.CreateAsync(
                channelId,
                new CreateTimerDto
                {
                    Name = "Discord plug",
                    Messages = ["Join the Discord!", "Seriously, join it."],
                    PipelineId = pipeline.Id,
                    IntervalMinutes = 45,
                    MinChatActivity = 7,
                    IsEnabled = false,
                    FireOnce = true,
                }
            )
        ).Value;

        ChatTriggerDto trigger = (
            await h.ChatTriggers.CreateAsync(
                channelId,
                new CreateChatTriggerRequest
                {
                    Pattern = "hello there",
                    MatchType = "exact",
                    CaseSensitive = true,
                    Response = "General {user}!",
                    PipelineId = pipeline.Id,
                    CooldownSeconds = 120,
                    MinPermissionLevel = 10,
                }
            )
        ).Value;

        PickListDto pickList = (
            await h.PickLists.CreateAsync(
                h.ActingChannel,
                new CreatePickListRequest(
                    "fight_moves",
                    "Fight lines",
                    ["{user} bonks {target}", "{user} yeets {target}"]
                )
            )
        ).Value;

        CodeScriptDetailDto script = (
            await h.CodeScripts.CreateAsync(
                new CreateCodeScriptRequest("greeter", "Greets chat", "export {};")
            )
        ).Value;
        Result<CodeScriptVersionDto> project = await h.CodeScripts.SaveProjectAsync(
            script.Id,
            new ProjectDto(
                new Dictionary<string, string>
                {
                    ["index.ts"] = "import { line } from './lib/lines'; api.chat.send(line());",
                    ["lib/lines.ts"] = "export const line = () => 'hi';",
                },
                new ProjectManifestDto("index.ts", "script", "typescript", [])
            )
        );
        project.IsSuccess.Should().BeTrue(project.ErrorMessage);

        return new SeededParityContent(
            pipeline.Id,
            eventResponse.Id,
            Guid.Parse(reward.Id),
            timer.Id,
            trigger.Id,
            pickList.Id,
            script.Id
        );
    }

    private static ExportRequest AllItemsRequest(SeededParityContent seeded) =>
        new(
            [
                new ExportItemRef(BundleFormat.EventResponseType, seeded.EventResponseId),
                new ExportItemRef(BundleFormat.RewardType, seeded.RewardId),
                new ExportItemRef(BundleFormat.TimerType, seeded.TimerId),
                new ExportItemRef(BundleFormat.ChatTriggerType, seeded.ChatTriggerId),
                new ExportItemRef(BundleFormat.PickListType, seeded.PickListId),
                new ExportItemRef(BundleFormat.CodeScriptType, seeded.CodeScriptId),
            ],
            new BundleMetadata("Parity Pack", "1.0.0", "stoney", "MIT", "full channel setup")
        );

    private static async Task<MemoryStream> ExportAllAsync(Harness h, SeededParityContent seeded)
    {
        Result<System.IO.Stream> zip = await h.Export.ExportAsync(
            h.ActingChannel,
            AllItemsRequest(seeded)
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

    // ── Export shape ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_writes_every_parity_type_with_pipeline_edges_and_no_twitch_or_tenant_fields()
    {
        Harness h = Build(Channel);
        SeededParityContent seeded = await SeedAllAsync(h);
        // The reward carries a Twitch linkage on the source — it must NOT travel (D2 allowlist).
        Reward sourceReward = await h.Db.Rewards.SingleAsync();
        sourceReward.TwitchRewardId = "twitch-reward-guid-1234";
        await h.Db.SaveChangesAsync();

        MemoryStream zip = await ExportAllAsync(h, seeded);
        Dictionary<string, string> entries = ReadEntries(zip);

        entries
            .Keys.Should()
            .Contain([
                "manifest.json",
                "pipelines/greeting-flow.json",
                "event-responses/channel-follow.json",
                "rewards/hydrate-.json",
                "timers/discord-plug.json",
                "chat-triggers/hello-there.json",
                "pick-lists/fight_moves.json",
                "code-scripts/greeter.json",
            ]);

        BundleManifest manifest = BundleConventions.Deserialize<BundleManifest>(
            entries["manifest.json"]
        )!;
        // 6 requested items + the auto-pulled pipeline.
        manifest.Items.Should().HaveCount(7);
        foreach (
            string type in (string[])
                [
                    BundleFormat.EventResponseType,
                    BundleFormat.RewardType,
                    BundleFormat.TimerType,
                    BundleFormat.ChatTriggerType,
                ]
        )
            manifest
                .Items.Single(i => i.Type == type)
                .Dependencies.Should()
                .ContainSingle()
                .Which.Should()
                .Be("pipeline:Greeting Flow");

        RewardExport reward = BundleConventions.Deserialize<RewardExport>(
            entries["rewards/hydrate-.json"]
        )!;
        reward.Title.Should().Be("Hydrate!");
        reward.Cost.Should().Be(250);
        reward.Description.Should().Be("Make the streamer drink water");
        reward.Response.Should().Be("{user} made me drink!");
        reward.TimerDurationSeconds.Should().Be(90);
        reward.PipelineName.Should().Be("Greeting Flow");
        entries["rewards/hydrate-.json"].Should().NotContain("twitch-reward-guid-1234");
        entries["rewards/hydrate-.json"].Should().NotContainEquivalentOf("twitchRewardId");
        entries.Values.Should().OnlyContain(text => !text.Contains(Channel.ToString()));

        CodeScriptExport script = BundleConventions.Deserialize<CodeScriptExport>(
            entries["code-scripts/greeter.json"]
        )!;
        script.Files.Should().ContainKeys("index.ts", "lib/lines.ts");
        script.Manifest.Entry.Should().Be("index.ts");
        script.DeclaredCapabilities.Should().BeEquivalentTo("chat.send", "http.fetch");
    }

    // ── Inspect ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inspect_enumerates_the_parity_types_and_surfaces_script_capabilities()
    {
        Harness h = Build(Channel);
        SeededParityContent seeded = await SeedAllAsync(h);
        MemoryStream zip = await ExportAllAsync(h, seeded);

        Result<BundleInspection> inspection = await h.Import.InspectAsync(Channel, zip);

        inspection.IsSuccess.Should().BeTrue(inspection.ErrorMessage);
        inspection.Value.Issues.Should().BeEmpty();
        Dictionary<string, int> counts = inspection
            .Value.Manifest.Items.GroupBy(i => i.Type)
            .ToDictionary(g => g.Key, g => g.Count());
        counts
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, int>
                {
                    [BundleFormat.PipelineType] = 1,
                    [BundleFormat.EventResponseType] = 1,
                    [BundleFormat.RewardType] = 1,
                    [BundleFormat.TimerType] = 1,
                    [BundleFormat.ChatTriggerType] = 1,
                    [BundleFormat.PickListType] = 1,
                    [BundleFormat.CodeScriptType] = 1,
                }
            );
        inspection.Value.Capabilities.Should().Contain("executes custom code");
        // The bundled script's declared capability keys, mapped next to the pipeline-action entries.
        inspection.Value.Capabilities.Should().Contain("sends chat messages");
        inspection.Value.Capabilities.Should().Contain("makes outbound HTTP requests");
        inspection
            .Value.Capabilities.Should()
            .Contain([
                "responds to channel events (overwrites the existing per-event responses)",
                "adds channel point rewards (created locally; synced to Twitch on demand)",
                "adds chat timers",
                "reacts to chat keywords",
                "adds pick lists",
            ]);
    }

    // ── Round trip into a different tenant ──────────────────────────────────────

    [Fact]
    public async Task Round_trip_recreates_every_parity_type_with_full_shape_and_relinks_pipelines_by_name()
    {
        Harness source = Build(Channel);
        SeededParityContent seeded = await SeedAllAsync(source);
        MemoryStream zip = await ExportAllAsync(source, seeded);

        Harness target = Build(OtherChannel);
        // The target already has the lazily-seeded disabled default for this event (module convention).
        target.Db.EventResponses.Add(
            new EventResponse
            {
                BroadcasterId = OtherChannel,
                EventType = "channel.follow",
                IsEnabled = false,
                ResponseType = "chat_message",
            }
        );
        await target.Db.SaveChangesAsync();

        Result<InstalledBundleDto> installed = await target.Import.ImportAsync(
            OtherChannel,
            Actor,
            zip,
            ImportConflictPolicy.Rename
        );
        installed.IsSuccess.Should().BeTrue(installed.ErrorMessage);

        PipelineEntity pipeline = await target.Db.Pipelines.SingleAsync();
        pipeline.BroadcasterId.Should().Be(OtherChannel);
        pipeline.Name.Should().Be("Greeting Flow");
        pipeline.Id.Should().NotBe(seeded.PipelineId);

        // event_response: UPSERTED into the seeded default row — one row, fully overwritten, re-linked.
        EventResponse eventResponse = await target.Db.EventResponses.SingleAsync();
        eventResponse.EventType.Should().Be("channel.follow");
        eventResponse.IsEnabled.Should().BeTrue();
        eventResponse.ResponseType.Should().Be("pipeline");
        eventResponse.Message.Should().Be("Thanks for the follow, {user}!");
        eventResponse.PipelineId.Should().Be(pipeline.Id);
        eventResponse.MetadataJson.Should().Contain("widget", "alert-box");

        // reward: a LOCAL manageable definition — full shape, no Twitch id, re-linked by name.
        Reward reward = await target.Db.Rewards.SingleAsync();
        reward.BroadcasterId.Should().Be(OtherChannel);
        reward.Title.Should().Be("Hydrate!");
        reward.Cost.Should().Be(250);
        reward.Description.Should().Be("Make the streamer drink water");
        reward.Response.Should().Be("{user} made me drink!");
        reward.TimerDurationSeconds.Should().Be(90);
        reward.IsEnabled.Should().BeTrue();
        reward.IsManageable.Should().BeTrue();
        reward.TwitchRewardId.Should().BeNull();
        reward.PipelineId.Should().Be(pipeline.Id);
        reward.Id.Should().NotBe(seeded.RewardId);

        DomainTimer timer = await target.Db.Timers.SingleAsync();
        timer.Name.Should().Be("Discord plug");
        timer.Messages.Should().Equal("Join the Discord!", "Seriously, join it.");
        timer.IntervalMinutes.Should().Be(45);
        timer.MinChatActivity.Should().Be(7);
        timer.FireOnce.Should().BeTrue();
        timer.IsEnabled.Should().BeFalse();
        timer.PipelineId.Should().Be(pipeline.Id);
        timer.Id.Should().NotBe(seeded.TimerId);

        ChatTrigger trigger = await target.Db.ChatTriggers.SingleAsync();
        trigger.Pattern.Should().Be("hello there");
        trigger.MatchType.Should().Be("exact");
        trigger.CaseSensitive.Should().BeTrue();
        trigger.Response.Should().Be("General {user}!");
        trigger.CooldownSeconds.Should().Be(120);
        trigger.MinPermissionLevel.Should().Be(10);
        trigger.IsEnabled.Should().BeTrue();
        trigger.PipelineId.Should().Be(pipeline.Id);
        trigger.Id.Should().NotBe(seeded.ChatTriggerId);

        PickList pickList = await target.Db.PickLists.SingleAsync();
        pickList.Name.Should().Be("fight_moves");
        pickList.Description.Should().Be("Fight lines");
        pickList.Items.Should().Equal("{user} bonks {target}", "{user} yeets {target}");
        pickList.Id.Should().NotBe(seeded.PickListId);

        // code_script: recreated + full project stored + published, and ALWAYS disabled (D4).
        CodeScript script = await target.Db.CodeScripts.SingleAsync();
        script.Name.Should().Be("greeter");
        script.Description.Should().Be("Greets chat");
        script.IsEnabled.Should().BeFalse();
        script.CurrentVersionId.Should().NotBeNull();
        script.Id.Should().NotBe(seeded.CodeScriptId);
        CodeScriptVersion current = await target.Db.CodeScriptVersions.SingleAsync(v =>
            v.Id == script.CurrentVersionId
        );
        current.FilesJson.Should().Contain("lib/lines.ts");
        current.DeclaredCapabilitiesJson.Should().Contain("chat.send");
        current.DeclaredCapabilitiesJson.Should().Contain("http.fetch");

        // The ledger row covers every installed entity so uninstall can remove them exactly.
        Domain.Marketplace.Entities.InstalledBundle row =
            await target.Db.InstalledBundles.SingleAsync();
        foreach (
            Guid id in (Guid[])
                [
                    pipeline.Id,
                    eventResponse.Id,
                    reward.Id,
                    timer.Id,
                    trigger.Id,
                    pickList.Id,
                    script.Id,
                ]
        )
            row.InstalledEntityIdsJson.Should().Contain(id.ToString());
    }

    // ── Collision behavior ──────────────────────────────────────────────────────

    [Fact]
    public async Task Importing_twice_renames_every_named_type_but_event_responses_upsert_in_place()
    {
        Harness source = Build(Channel);
        SeededParityContent seeded = await SeedAllAsync(source);
        MemoryStream zip = await ExportAllAsync(source, seeded);

        Harness target = Build(OtherChannel);
        Result<InstalledBundleDto> first = await target.Import.ImportAsync(
            OtherChannel,
            Actor,
            zip,
            ImportConflictPolicy.Rename
        );
        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        zip.Position = 0;
        Result<InstalledBundleDto> second = await target.Import.ImportAsync(
            OtherChannel,
            Actor,
            zip,
            ImportConflictPolicy.Rename
        );
        second.IsSuccess.Should().BeTrue(second.ErrorMessage);

        // Free-text names get " (bundle)"; the slug-typed pick-list name gets "-bundle".
        (await target.Db.Pipelines.Select(p => p.Name).ToListAsync())
            .Should()
            .BeEquivalentTo("Greeting Flow", "Greeting Flow (bundle)");
        (await target.Db.Rewards.Select(r => r.Title).ToListAsync())
            .Should()
            .BeEquivalentTo("Hydrate!", "Hydrate! (bundle)");
        (await target.Db.Timers.Select(t => t.Name).ToListAsync())
            .Should()
            .BeEquivalentTo("Discord plug", "Discord plug (bundle)");
        (await target.Db.ChatTriggers.Select(t => t.Pattern).ToListAsync())
            .Should()
            .BeEquivalentTo("hello there", "hello there (bundle)");
        (await target.Db.PickLists.Select(p => p.Name).ToListAsync())
            .Should()
            .BeEquivalentTo("fight_moves", "fight_moves-bundle");
        (await target.Db.CodeScripts.Select(s => s.Name).ToListAsync())
            .Should()
            .BeEquivalentTo("greeter", "greeter (bundle)");

        // Event responses are keyed per event — the second import overwrote the same single row.
        EventResponse eventResponse = await target.Db.EventResponses.SingleAsync();
        eventResponse.EventType.Should().Be("channel.follow");
        // The re-import re-linked the response to the SECOND import's pipeline.
        PipelineEntity renamedPipeline = await target.Db.Pipelines.SingleAsync(p =>
            p.Name == "Greeting Flow (bundle)"
        );
        eventResponse.PipelineId.Should().Be(renamedPipeline.Id);
    }

    // ── Failure modes ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Bundle_referencing_a_pipeline_not_in_the_bundle_fails_typed_before_any_write()
    {
        Harness h = Build(Channel);
        using MemoryStream zip = BuildZip(
            (
                "manifest.json",
                """
                {"schemaVersion":1,"metadata":{"name":"Broken Pack","version":"1.0.0"},
                 "items":[{"type":"timer","name":"Hydrate","path":"timers/hydrate.json","dependencies":["pipeline:Ghost Flow"]}]}
                """
            ),
            (
                "timers/hydrate.json",
                """{"schemaVersion":1,"name":"Hydrate","messages":["drink"],"intervalMinutes":15,"pipelineName":"Ghost Flow","isEnabled":true}"""
            )
        );

        Result<BundleInspection> inspection = await h.Import.InspectAsync(Channel, zip);
        inspection.IsSuccess.Should().BeTrue(inspection.ErrorMessage);
        inspection
            .Value.Issues.Should()
            .ContainSingle(i => i.Contains("Ghost Flow") && i.Contains("not in the bundle"));

        zip.Position = 0;
        Result<InstalledBundleDto> installed = await h.Import.ImportAsync(
            Channel,
            Actor,
            zip,
            ImportConflictPolicy.Rename
        );
        installed.IsFailure.Should().BeTrue();
        installed.ErrorCode.Should().Be("BUNDLE_INVALID");
        (await h.Db.Timers.CountAsync()).Should().Be(0);
        (await h.Db.InstalledBundles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Unknown_manifest_item_type_fails_typed_and_installs_nothing()
    {
        Harness h = Build(Channel);
        using MemoryStream zip = BuildZip(
            (
                "manifest.json",
                """
                {"schemaVersion":1,"metadata":{"name":"Weird Pack","version":"1.0.0"},
                 "items":[{"type":"flux_capacitor","name":"Doc","path":"flux/doc.json","dependencies":[]}]}
                """
            ),
            ("flux/doc.json", """{"schemaVersion":1,"name":"Doc"}""")
        );

        Result<InstalledBundleDto> installed = await h.Import.ImportAsync(
            Channel,
            Actor,
            zip,
            ImportConflictPolicy.Rename
        );

        installed.IsFailure.Should().BeTrue();
        installed.ErrorCode.Should().Be("BUNDLE_INVALID");
        installed.ErrorMessage.Should().Contain("flux_capacitor");
        (await h.Db.InstalledBundles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Mid_bundle_failure_rolls_back_everything_already_created()
    {
        Harness h = Build(Channel);
        // Reward + timer import first; the chat trigger's invalid regex then fails inside the module
        // service (compile-checked at write time) — the whole install must roll back.
        using MemoryStream zip = BuildZip(
            (
                "manifest.json",
                """
                {"schemaVersion":1,"metadata":{"name":"Bad Pack","version":"1.0.0"},
                 "items":[
                   {"type":"reward","name":"Hydrate!","path":"rewards/hydrate.json","dependencies":[]},
                   {"type":"timer","name":"Hydrate","path":"timers/hydrate.json","dependencies":[]},
                   {"type":"chat_trigger","name":"(","path":"chat-triggers/bad.json","dependencies":[]}]}
                """
            ),
            (
                "rewards/hydrate.json",
                """{"schemaVersion":1,"title":"Hydrate!","cost":100,"isEnabled":true}"""
            ),
            (
                "timers/hydrate.json",
                """{"schemaVersion":1,"name":"Hydrate","messages":["drink"],"intervalMinutes":15,"isEnabled":true}"""
            ),
            (
                "chat-triggers/bad.json",
                """{"schemaVersion":1,"pattern":"(","matchType":"regex","cooldownSeconds":5,"isEnabled":true}"""
            )
        );

        Result<InstalledBundleDto> installed = await h.Import.ImportAsync(
            Channel,
            Actor,
            zip,
            ImportConflictPolicy.Rename
        );

        installed.IsFailure.Should().BeTrue();
        installed.ErrorMessage.Should().Contain("rolled back");
        (await h.Db.Rewards.CountAsync()).Should().Be(0);
        (await h.Db.Timers.CountAsync()).Should().Be(0);
        (await h.Db.ChatTriggers.CountAsync()).Should().Be(0);
        (await h.Db.InstalledBundles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Rejected_code_script_compile_rolls_back_including_the_audit_remnant()
    {
        Harness source = Build(Channel);
        SeededParityContent seeded = await SeedAllAsync(source);
        MemoryStream zip = await ExportAllAsync(source, seeded);

        Harness target = Build(OtherChannel);
        // The TARGET instance recompiles on import (validate-on-save) — make its compiler reject.
        target
            .Executor.CompileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ScriptCompilation>("nope", "VALIDATION_FAILED"));

        Result<InstalledBundleDto> installed = await target.Import.ImportAsync(
            OtherChannel,
            Actor,
            zip,
            ImportConflictPolicy.Rename
        );

        installed.IsFailure.Should().BeTrue();
        installed.ErrorMessage.Should().Contain("rolled back");
        // Everything the bundle created is gone — including the rejected script's live row.
        (await target.Db.Pipelines.CountAsync())
            .Should()
            .Be(0);
        (await target.Db.Rewards.CountAsync()).Should().Be(0);
        (await target.Db.Timers.CountAsync()).Should().Be(0);
        (await target.Db.ChatTriggers.CountAsync()).Should().Be(0);
        (await target.Db.PickLists.CountAsync()).Should().Be(0);
        (await target.Db.EventResponses.CountAsync()).Should().Be(0);
        (await target.Db.CodeScripts.CountAsync()).Should().Be(0);
        (await target.Db.InstalledBundles.CountAsync()).Should().Be(0);
    }

    // ── Uninstall ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Uninstall_removes_every_parity_entity_and_the_ledger_row()
    {
        Harness source = Build(Channel);
        SeededParityContent seeded = await SeedAllAsync(source);
        MemoryStream zip = await ExportAllAsync(source, seeded);

        Harness target = Build(OtherChannel);
        InstalledBundleDto installed = (
            await target.Import.ImportAsync(OtherChannel, Actor, zip, ImportConflictPolicy.Rename)
        ).Value;

        // A pre-existing, unrelated pick list must survive the uninstall untouched.
        PickListDto keeper = (
            await target.PickLists.CreateAsync(
                OtherChannel,
                new CreatePickListRequest("keep_me", null, ["stays"])
            )
        ).Value;

        Result uninstalled = await target.Import.UninstallAsync(OtherChannel, installed.Id, Actor);

        uninstalled.IsSuccess.Should().BeTrue(uninstalled.ErrorMessage);
        (await target.Db.Pipelines.CountAsync()).Should().Be(0);
        (await target.Db.EventResponses.CountAsync()).Should().Be(0);
        (await target.Db.Rewards.CountAsync()).Should().Be(0);
        (await target.Db.Timers.CountAsync()).Should().Be(0);
        (await target.Db.ChatTriggers.CountAsync()).Should().Be(0);
        (await target.Db.CodeScripts.CountAsync()).Should().Be(0);
        PickList survivor = await target.Db.PickLists.SingleAsync(p => p.DeletedAt == null);
        survivor.Id.Should().Be(keeper.Id);
        (await target.Db.InstalledBundles.CountAsync()).Should().Be(0);
    }
}
