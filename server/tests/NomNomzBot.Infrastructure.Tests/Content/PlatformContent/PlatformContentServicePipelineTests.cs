// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.PlatformContent;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Content.PlatformContent;

/// <summary>
/// S-ADMIN-2d — the <c>pipeline</c> kind on the platform-content spine (platform-admin.md §2.1-§2.2). Runs
/// <see cref="PlatformContentService"/> against a REAL <see cref="PipelineService"/> (the exact same
/// create/validate/persist path the dashboard's pipeline tree editor uses) and, for the execution proof, a
/// real <see cref="PipelineEngine"/> — all sharing one relational SQLite <see cref="PlatformContentTestDbContext"/>,
/// so "the tenant's pipeline actually runs the new graph" is proven by really running it, never inferred from
/// a row value.
/// </summary>
public sealed class PlatformContentServicePipelineTests : IAsyncDisposable
{
    private readonly PlatformContentTestDbContext _db = PlatformContentTestDbContext.New();
    private readonly IPlatformIamService _iam = Substitute.For<IPlatformIamService>();
    private readonly Guid _actingPrincipalId = Guid.NewGuid();

    /// <summary>Records every marker a published graph's <c>record_marker</c> step ran with — the
    /// execution-proof action (below), registered into both the validator and the engine.</summary>
    private readonly List<string> _recordedMarkers = [];

    private sealed class RecordMarkerAction(List<string> sink) : ICommandAction
    {
        public string ActionType => "record_marker";
        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            NomNomzBot.Application.Abstractions.Pipeline.ActionDefinition action
        )
        {
            sink.Add(action.GetString("marker") ?? "");
            return Task.FromResult(ActionResult.Success());
        }
    }

    private CommandConfigValidator CreateValidator() =>
        new([new RecordMarkerAction(_recordedMarkers)], new TemplateHelperValidator());

    private PipelineService CreatePipelineService() =>
        new(
            _db,
            new TestUnitOfWork(_db),
            Substitute.For<IEventBus>(),
            CreateValidator(),
            Substitute.For<IChannelRegistry>()
        );

    private PlatformContentService CreateService() =>
        new(
            _db,
            _iam,
            new TestUnitOfWork(_db),
            Substitute.For<IVueSfcCompiler>(),
            Substitute.For<IWidgetService>(),
            CreatePipelineService(),
            Substitute.For<IScriptExecutor>()
        );

    private IPipelineEngine CreateEngine() =>
        new PipelineEngine(
            _db,
            Substitute.For<IChannelRegistry>(),
            [new RecordMarkerAction(_recordedMarkers)],
            [],
            Substitute.For<ITemplateResolver>(),
            NullLogger<PipelineEngine>.Instance,
            TimeProvider.System
        );

    public PlatformContentServicePipelineTests()
    {
        _iam.AuthorizePlatformAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Guid?>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            )
            .Returns(Result.Success(true));
    }

    private static string GraphPayload(string marker) =>
        JsonSerializer.Serialize(
            new { steps = new object[] { new { action = new { type = "record_marker", marker } } } }
        );

    /// <summary>A payload whose action type no validator here (or in production) has ever registered —
    /// used to prove the per-tenant validation-failure path.</summary>
    private static string InvalidGraphPayload() =>
        JsonSerializer.Serialize(
            new { steps = new object[] { new { action = new { type = "no_such_action_type" } } } }
        );

    private async Task<Channel> AddChannelAsync(string name)
    {
        Channel channel = new()
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            NameNormalized = name.ToLowerInvariant(),
        };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();
        return channel;
    }

    /// <summary>Seeds a tenant pipeline the way the raid seeders do (a real graph run through
    /// <see cref="PipelineService"/>'s own persistence, not a hand-built row), then stamps provenance the
    /// way the fan-out itself would after a successful publish — the row this test starts from is exactly
    /// the shape a real installed/seeded pipeline is in before its NEXT publish.</summary>
    private async Task<PipelineEntity> SeedInstalledPipelineAsync(
        Guid broadcasterId,
        PlatformContentDefinition definition,
        PlatformContentVersion installedVersion,
        string marker
    )
    {
        PipelineService pipelines = CreatePipelineService();
        Result<PipelineDto> created = await pipelines.CreateAsync(
            broadcasterId.ToString(),
            new CreatePipelineDto
            {
                Name = "Raid out",
                TriggerKind = "command",
                GraphJsonCache = JsonSerializer.Deserialize<JsonElement>(GraphPayload(marker)),
            }
        );
        Assert.True(created.IsSuccess, created.ErrorMessage);

        PipelineEntity row = await _db.Pipelines.SingleAsync(p => p.Id == created.Value.Id);
        row.PlatformSourceDefinitionId = definition.Id;
        row.PlatformSourceVersion = installedVersion.Version;
        row.PlatformSourceHash = installedVersion.ContentHash;
        row.PlatformSourceSyncedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return row;
    }

    private async Task<(
        PlatformContentDefinition Definition,
        PlatformContentVersion V1
    )> SeedPublishedPipelineDefinitionAsync(string key = "raid_out")
    {
        string payload = GraphPayload("v1");
        PlatformContentDefinition definition = new()
        {
            Kind = PlatformContentKinds.Pipeline,
            Key = key,
            DisplayName = key,
            CreatedAt = DateTime.UtcNow,
            CreatedByPrincipalId = _actingPrincipalId,
        };
        _db.PlatformContentDefinitions.Add(definition);

        PlatformContentVersion v1 = new()
        {
            DefinitionId = definition.Id,
            Version = 1,
            ContentHash = PlatformContentHash.ComputeHash(payload),
            PayloadJson = payload,
            DraftedAt = DateTime.UtcNow,
            DraftedByPrincipalId = _actingPrincipalId,
            PublishedAt = DateTime.UtcNow,
            PublishedByPrincipalId = _actingPrincipalId,
        };
        _db.PlatformContentVersions.Add(v1);
        definition.CurrentVersionId = v1.Id;
        definition.LatestDraftVersionId = v1.Id;
        await _db.SaveChangesAsync();

        return (definition, v1);
    }

    private async Task<PlatformContentVersion> DraftVersionAsync(
        PlatformContentDefinition definition,
        int version,
        string payloadJson
    )
    {
        PlatformContentVersion v = new()
        {
            DefinitionId = definition.Id,
            Version = version,
            ContentHash = PlatformContentHash.ComputeHash(payloadJson),
            PayloadJson = payloadJson,
            DraftedAt = DateTime.UtcNow,
            DraftedByPrincipalId = _actingPrincipalId,
        };
        _db.PlatformContentVersions.Add(v);
        await _db.SaveChangesAsync();
        return v;
    }

    // ---------------------------------------------------------------------------------------------------
    // DONE-WHEN 1: an untouched tenant takes the new graph; a tenant who edited theirs keeps it.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task UpdateInPlaceWhereUntouched_TakesNewGraph_ForUntouchedTenant_LeavesEditedTenantAlone()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedPipelineDefinitionAsync();

        Channel untouchedChannel = await AddChannelAsync("untouched-streamer");
        PipelineEntity untouchedPipeline = await SeedInstalledPipelineAsync(
            untouchedChannel.Id,
            definition,
            v1,
            "v1"
        );
        // The row's PlatformSourceHash must match its OWN live GraphJsonCache to read as "untouched" —
        // SeedInstalledPipelineAsync stamps the hash from the DEFINITION's v1 (built from the SAME "v1"
        // marker), so this is genuinely untouched, not merely labelled so.

        Channel editedChannel = await AddChannelAsync("edited-streamer");
        PipelineEntity editedPipeline = await SeedInstalledPipelineAsync(
            editedChannel.Id,
            definition,
            v1,
            "v1"
        );
        // Simulate the streamer editing their own copy through the SAME PipelineService a real dashboard
        // save would use — never a hand-poked column.
        PipelineService pipelines = CreatePipelineService();
        Result<PipelineDto> edited = await pipelines.UpdateAsync(
            editedChannel.Id.ToString(),
            editedPipeline.Id,
            new UpdatePipelineDto
            {
                GraphJsonCache = JsonSerializer.Deserialize<JsonElement>(
                    GraphPayload("edited-by-streamer")
                ),
            }
        );
        Assert.True(edited.IsSuccess, edited.ErrorMessage);

        PlatformContentVersion v2 = await DraftVersionAsync(definition, 2, GraphPayload("v2"));

        PlatformContentService sut = CreateService();
        Result<PlatformContentPublishJobDto> publishResult = await sut.PublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            new PublishContentRequest(
                PlatformContentPublishModes.UpdateInPlaceWhereUntouched,
                PublishNote: null,
                ConfirmedPreviewAffectedCount: 1
            )
        );

        Assert.True(publishResult.IsSuccess, publishResult.ErrorMessage);
        Assert.Equal(PlatformContentPublishJobStatuses.Completed, publishResult.Value.Status);
        Assert.Equal(1, publishResult.Value.ConfirmedAffectedCount);
        Assert.Empty(publishResult.Value.ValidationFailedPipelineIds);

        PipelineEntity untouchedAfter = await _db
            .Pipelines.AsNoTracking()
            .SingleAsync(p => p.Id == untouchedPipeline.Id);
        Assert.Contains("v2", untouchedAfter.GraphJsonCache);
        Assert.Equal(2, untouchedAfter.PlatformSourceVersion);
        Assert.Equal(v2.ContentHash, untouchedAfter.PlatformSourceHash);

        PipelineEntity editedAfter = await _db
            .Pipelines.AsNoTracking()
            .SingleAsync(p => p.Id == editedPipeline.Id);
        Assert.Contains("edited-by-streamer", editedAfter.GraphJsonCache);
        Assert.DoesNotContain("v2", editedAfter.GraphJsonCache);
        Assert.Equal(1, editedAfter.PlatformSourceVersion);
    }

    // ---------------------------------------------------------------------------------------------------
    // DONE-WHEN 2: the blast-radius preview returns correct counts from real linked tenant rows — the
    // exact check the widget kind's "always zero" bug (S-ADMIN-2c, fixed S-ADMIN-2c-b) would have caught.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task PreviewPublish_ReturnsRealCounts_NeverZeroWhenTenantsAreActuallyLinked()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedPipelineDefinitionAsync();

        Channel untouched1 = await AddChannelAsync("untouched-1");
        await SeedInstalledPipelineAsync(untouched1.Id, definition, v1, "v1");
        Channel untouched2 = await AddChannelAsync("untouched-2");
        await SeedInstalledPipelineAsync(untouched2.Id, definition, v1, "v1");

        Channel editedChannel = await AddChannelAsync("edited");
        PipelineEntity editedPipeline = await SeedInstalledPipelineAsync(
            editedChannel.Id,
            definition,
            v1,
            "v1"
        );
        PipelineService pipelines = CreatePipelineService();
        await pipelines.UpdateAsync(
            editedChannel.Id.ToString(),
            editedPipeline.Id,
            new UpdatePipelineDto
            {
                GraphJsonCache = JsonSerializer.Deserialize<JsonElement>(
                    GraphPayload("streamer-edit")
                ),
            }
        );

        // A pipeline this definition never installed — must never be counted.
        Channel neverInstalled = await AddChannelAsync("never-installed");
        PipelineService neverInstalledPipelines = CreatePipelineService();
        await neverInstalledPipelines.CreateAsync(
            neverInstalled.Id.ToString(),
            new CreatePipelineDto
            {
                Name = "Unrelated",
                GraphJsonCache = JsonSerializer.Deserialize<JsonElement>(GraphPayload("unrelated")),
            }
        );

        PlatformContentVersion v2 = await DraftVersionAsync(definition, 2, GraphPayload("v2"));

        PlatformContentService sut = CreateService();
        Result<PublishPreviewDto> preview = await sut.PreviewPublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            PlatformContentPublishModes.UpdateInPlaceWhereUntouched
        );

        Assert.True(preview.IsSuccess, preview.ErrorMessage);
        Assert.Equal(2, preview.Value.AffectedCount);
        Assert.Equal(1, preview.Value.SkippedCount);
        Assert.NotEqual(0, preview.Value.AffectedCount); // the exact "always zero" regression guard

        // Nothing written by a preview.
        Assert.Equal(0, await _db.PlatformContentPublishJobs.CountAsync());
        PipelineEntity untouchedStillOnV1 = await _db
            .Pipelines.AsNoTracking()
            .FirstAsync(p => p.PlatformSourceDefinitionId == definition.Id);
        Assert.Equal(1, untouchedStillOnV1.PlatformSourceVersion);
    }

    // ---------------------------------------------------------------------------------------------------
    // DONE-WHEN 3: an untouched tenant's pipeline actually EXECUTES the new graph after a publish — proven
    // through the real PipelineEngine, not by reading a persisted column.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Publish_UntouchedTenantsPipelineExecutesTheNewGraph_ThroughTheRealEngine()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedPipelineDefinitionAsync();

        Channel channel = await AddChannelAsync("streamer");
        PipelineEntity pipeline = await SeedInstalledPipelineAsync(
            channel.Id,
            definition,
            v1,
            "v1"
        );

        PlatformContentVersion v2 = await DraftVersionAsync(
            definition,
            2,
            GraphPayload("v2-marker")
        );

        PlatformContentService sut = CreateService();
        Result<PlatformContentPublishJobDto> publishResult = await sut.PublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            new PublishContentRequest(
                PlatformContentPublishModes.UpdateInPlaceWhereUntouched,
                PublishNote: null,
                ConfirmedPreviewAffectedCount: 1
            )
        );
        Assert.True(publishResult.IsSuccess, publishResult.ErrorMessage);

        _recordedMarkers.Clear(); // discard anything CreateAsync's own steps might have touched

        IPipelineEngine engine = CreateEngine();
        PipelineExecutionResult execution = await engine.ExecuteAsync(
            new PipelineRequest
            {
                BroadcasterId = channel.Id,
                PipelineId = pipeline.Id,
                TriggeredByUserId = Guid.NewGuid().ToString(),
                TriggeredByDisplayName = "tester",
            }
        );

        Assert.Equal(PipelineOutcome.Completed, execution.Outcome);
        Assert.Equal(["v2-marker"], _recordedMarkers);
    }

    // ---------------------------------------------------------------------------------------------------
    // DONE-WHEN 4: a tenant whose published graph fails validation keeps its previously working pipeline,
    // and the failure is recorded — never left with a broken pipeline.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Publish_WhenNewGraphFailsValidation_TenantKeepsWorkingPipeline_AndFailureIsRecorded()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedPipelineDefinitionAsync();

        Channel channel = await AddChannelAsync("streamer");
        PipelineEntity pipeline = await SeedInstalledPipelineAsync(
            channel.Id,
            definition,
            v1,
            "v1"
        );
        string graphBeforePublish = (
            await _db.Pipelines.AsNoTracking().SingleAsync(p => p.Id == pipeline.Id)
        ).GraphJsonCache!;

        PlatformContentVersion badVersion = await DraftVersionAsync(
            definition,
            2,
            InvalidGraphPayload()
        );

        PlatformContentService sut = CreateService();
        Result<PlatformContentPublishJobDto> publishResult = await sut.PublishAsync(
            _actingPrincipalId,
            definition.Id,
            badVersion.Id,
            new PublishContentRequest(
                PlatformContentPublishModes.UpdateInPlaceWhereUntouched,
                PublishNote: null,
                ConfirmedPreviewAffectedCount: 1
            )
        );

        // The publish job itself completes (a per-tenant validation failure is recorded, never an
        // unhandled exception that aborts the whole fan-out) but confirms zero tenants actually moved.
        Assert.True(publishResult.IsSuccess, publishResult.ErrorMessage);
        Assert.Equal(PlatformContentPublishJobStatuses.Completed, publishResult.Value.Status);
        Assert.Equal(0, publishResult.Value.ConfirmedAffectedCount);
        Assert.Contains(pipeline.Id, publishResult.Value.ValidationFailedPipelineIds);

        PipelineEntity after = await _db
            .Pipelines.AsNoTracking()
            .SingleAsync(p => p.Id == pipeline.Id);
        Assert.Equal(graphBeforePublish, after.GraphJsonCache);
        Assert.Equal(1, after.PlatformSourceVersion);
        Assert.Equal(v1.ContentHash, after.PlatformSourceHash);

        // The pipeline still runs its OLD, working graph — never left broken.
        _recordedMarkers.Clear();
        IPipelineEngine engine = CreateEngine();
        PipelineExecutionResult execution = await engine.ExecuteAsync(
            new PipelineRequest
            {
                BroadcasterId = channel.Id,
                PipelineId = pipeline.Id,
                TriggeredByUserId = Guid.NewGuid().ToString(),
                TriggeredByDisplayName = "tester",
            }
        );
        Assert.Equal(PipelineOutcome.Completed, execution.Outcome);
        Assert.Equal(["v1"], _recordedMarkers);
    }

    // ---------------------------------------------------------------------------------------------------
    // DONE-WHEN 5: the publish writes the audit record platform-admin.md §5 specifies.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Publish_WritesAuditRecordWithAffectedCountAndJobId()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedPipelineDefinitionAsync();

        Channel channel = await AddChannelAsync("streamer");
        await SeedInstalledPipelineAsync(channel.Id, definition, v1, "v1");

        PlatformContentVersion v2 = await DraftVersionAsync(definition, 2, GraphPayload("v2"));

        PlatformContentService sut = CreateService();
        Result<PlatformContentPublishJobDto> publishResult = await sut.PublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            new PublishContentRequest(
                PlatformContentPublishModes.UpdateInPlaceWhereUntouched,
                null,
                1
            )
        );
        Assert.True(publishResult.IsSuccess, publishResult.ErrorMessage);
        Guid jobId = publishResult.Value.Id;

        IamAuditLog auditRow = await _db
            .IamAuditLogs.AsNoTracking()
            .SingleAsync(a => a.PublishJobId == jobId);
        Assert.Equal("content:publish", auditRow.Permission);
        Assert.Equal($"pipeline:raid_out@v2", auditRow.TargetResource);
        Assert.Equal(1, auditRow.AffectedTenantCount);
        Assert.Equal(IamOutcome.Allowed, auditRow.Outcome);
        Assert.Equal(_actingPrincipalId, auditRow.PrincipalId);
    }

    // ---------------------------------------------------------------------------------------------------
    // DONE-WHEN (S-ADMIN-2d follow-up 1): the blast-radius preview returns the REAL counted number of
    // affected tenants — seed exactly 3, assert exactly 3 (the wording of the slice's own done-when).
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task PreviewPublish_SeedingThreeUntouchedTenants_ReturnsExactlyThree()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedPipelineDefinitionAsync();

        await SeedInstalledPipelineAsync(
            (await AddChannelAsync("tenant-1")).Id,
            definition,
            v1,
            "v1"
        );
        await SeedInstalledPipelineAsync(
            (await AddChannelAsync("tenant-2")).Id,
            definition,
            v1,
            "v1"
        );
        await SeedInstalledPipelineAsync(
            (await AddChannelAsync("tenant-3")).Id,
            definition,
            v1,
            "v1"
        );

        PlatformContentVersion v2 = await DraftVersionAsync(definition, 2, GraphPayload("v2"));

        PlatformContentService sut = CreateService();
        Result<PublishPreviewDto> preview = await sut.PreviewPublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            PlatformContentPublishModes.UpdateInPlaceWhereUntouched
        );

        Assert.True(preview.IsSuccess, preview.ErrorMessage);
        Assert.Equal(3, preview.Value.AffectedCount);
        Assert.Equal(0, preview.Value.SkippedCount);

        // A stale confirmed count (from a preview run before this one, or simply the wrong number) must
        // fail closed rather than silently publish to a different set of tenants than what was shown.
        Result<PlatformContentPublishJobDto> stalePublish = await sut.PublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            new PublishContentRequest(
                PlatformContentPublishModes.UpdateInPlaceWhereUntouched,
                PublishNote: null,
                ConfirmedPreviewAffectedCount: 2
            )
        );
        Assert.True(stalePublish.IsFailure);
        Assert.Equal("PREVIEW_STALE", stalePublish.ErrorCode);
        Assert.Equal(0, await _db.PlatformContentPublishJobs.CountAsync());
    }

    // ---------------------------------------------------------------------------------------------------
    // DONE-WHEN (S-ADMIN-2d follow-up 2): the seeder-law's third case for the pipeline kind — a tenant who
    // DELETED their pipeline must never have it resurrected by a publish. "Untouched receives" and
    // "customised is skipped" are already proven above; this closes the third case the slice named
    // explicitly. Relies on PlatformContentTestDbContext now composing the SAME soft-delete global query
    // filter production's AppDbContext applies (see that file) — without it this failed by finding and
    // reviving the deleted row.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Publish_DoesNotResurrectATenantsDeletedPipeline()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedPipelineDefinitionAsync();

        Channel channel = await AddChannelAsync("deleted-streamer");
        PipelineEntity deletedPipeline = await SeedInstalledPipelineAsync(
            channel.Id,
            definition,
            v1,
            "v1"
        );
        string graphBeforeDelete = deletedPipeline.GraphJsonCache!;
        deletedPipeline.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        PlatformContentVersion v2 = await DraftVersionAsync(definition, 2, GraphPayload("v2"));

        PlatformContentService sut = CreateService();
        Result<PublishPreviewDto> preview = await sut.PreviewPublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            PlatformContentPublishModes.UpdateInPlaceWhereUntouched
        );
        Assert.True(preview.IsSuccess, preview.ErrorMessage);
        Assert.Equal(0, preview.Value.AffectedCount); // the deleted tenant must never be counted

        Result<PlatformContentPublishJobDto> publishResult = await sut.PublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            new PublishContentRequest(
                PlatformContentPublishModes.UpdateInPlaceWhereUntouched,
                PublishNote: null,
                ConfirmedPreviewAffectedCount: 0
            )
        );
        Assert.True(publishResult.IsSuccess, publishResult.ErrorMessage);
        Assert.Equal(0, publishResult.Value.ConfirmedAffectedCount);

        // Assert the RESULTING PER-TENANT STATE directly against the row (bypassing the query filter, since
        // the row is SUPPOSED to be excluded from ordinary reads) — it must still read as deleted, and its
        // graph must be exactly what it was before the delete: never touched, never resurrected.
        PipelineEntity afterPublish = await _db
            .Pipelines.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(p => p.Id == deletedPipeline.Id);
        Assert.NotNull(afterPublish.DeletedAt);
        Assert.Equal(graphBeforeDelete, afterPublish.GraphJsonCache);
        Assert.Equal(1, afterPublish.PlatformSourceVersion);
    }

    // ---------------------------------------------------------------------------------------------------
    // DONE-WHEN (S-ADMIN-2d follow-up 3): a published pipeline with NESTED blocks survives save + reload
    // with its tree intact — the exact case 934355cb's CommandConfigValidator fix protects (a block-kind
    // step's placeholder Action.Type must never be validated as a known action type, or every nested
    // pipeline is rejected on save). Proven through the platform-content publish path specifically, not
    // just the tenant editor's own save, since the fan-out is what this slice adds.
    // ---------------------------------------------------------------------------------------------------

    private static string NestedIfGraphPayload(string thenMarker) =>
        JsonSerializer.Serialize(
            new
            {
                steps = new object[]
                {
                    // A block-kind step with NO condition rows: PipelineEngine treats a conditionless "if"
                    // as unconditionally true, so this exercises real nested dispatch without needing any
                    // ICommandCondition registered in this test's minimal engine.
                    new
                    {
                        id = "if1",
                        block_kind = "if",
                        order = 0,
                        action = new { type = "block" },
                    },
                    new
                    {
                        id = "leaf1",
                        parent_step_id = "if1",
                        branch = "then",
                        order = 0,
                        action = new { type = "record_marker", marker = thenMarker },
                    },
                },
            }
        );

    [Fact]
    public async Task Publish_NestedIfBlockPipeline_SurvivesSaveAndReload_WithItsTreeIntact()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedPipelineDefinitionAsync();

        Channel channel = await AddChannelAsync("nested-streamer");
        PipelineEntity pipeline = await SeedInstalledPipelineAsync(
            channel.Id,
            definition,
            v1,
            "v1"
        );

        PlatformContentVersion nestedVersion = await DraftVersionAsync(
            definition,
            2,
            NestedIfGraphPayload("nested-then")
        );

        PlatformContentService sut = CreateService();
        Result<PlatformContentPublishJobDto> publishResult = await sut.PublishAsync(
            _actingPrincipalId,
            definition.Id,
            nestedVersion.Id,
            new PublishContentRequest(
                PlatformContentPublishModes.UpdateInPlaceWhereUntouched,
                PublishNote: null,
                ConfirmedPreviewAffectedCount: 1
            )
        );

        // This is exactly the regression 934355cb fixed: a block-kind step's placeholder Action.Type used
        // to get validated as a real action type, rejecting the save with "Unknown action type 'block'".
        Assert.True(publishResult.IsSuccess, publishResult.ErrorMessage);
        Assert.Equal(1, publishResult.Value.ConfirmedAffectedCount);
        Assert.Empty(publishResult.Value.ValidationFailedPipelineIds);

        // Reload through the SAME PipelineService.GetAsync the dashboard's own editor uses — not the raw
        // GraphJsonCache column read directly — so the round trip is proven through real persistence.
        PipelineService pipelines = CreatePipelineService();
        Result<PipelineDto> reloaded = await pipelines.GetAsync(channel.Id.ToString(), pipeline.Id);
        Assert.True(reloaded.IsSuccess, reloaded.ErrorMessage);

        JsonElement graph = reloaded.Value.GraphJsonCache!.Value;
        List<JsonElement> steps = [.. graph.GetProperty("steps").EnumerateArray()];
        Assert.Equal(2, steps.Count);

        JsonElement ifStep = steps.Single(s =>
            s.TryGetProperty("block_kind", out JsonElement bk) && bk.GetString() == "if"
        );
        JsonElement leafStep = steps.Single(s =>
            s.TryGetProperty("action", out JsonElement a)
            && a.TryGetProperty("type", out JsonElement t)
            && t.GetString() == "record_marker"
        );
        Assert.Equal(
            ifStep.GetProperty("id").GetString(),
            leafStep.GetProperty("parent_step_id").GetString()
        );
        Assert.Equal("then", leafStep.GetProperty("branch").GetString());

        // The nested tree does not merely round-trip in storage — it actually EXECUTES: the leaf inside
        // the "then" lane runs through the real engine.
        _recordedMarkers.Clear();
        IPipelineEngine engine = CreateEngine();
        PipelineExecutionResult execution = await engine.ExecuteAsync(
            new PipelineRequest
            {
                BroadcasterId = channel.Id,
                PipelineId = pipeline.Id,
                TriggeredByUserId = Guid.NewGuid().ToString(),
                TriggeredByDisplayName = "tester",
            }
        );
        Assert.Equal(PipelineOutcome.Completed, execution.Outcome);
        Assert.Equal(["nested-then"], _recordedMarkers);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();
}
