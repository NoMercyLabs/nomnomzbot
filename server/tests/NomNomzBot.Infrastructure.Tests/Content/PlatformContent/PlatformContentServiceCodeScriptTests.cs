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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Infrastructure.Content.PlatformContent;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Content.PlatformContent;

/// <summary>
/// S-ADMIN-2e — the <c>code_script</c> kind on the platform-content spine (platform-admin.md §2.1-§2.2). Runs
/// <see cref="PlatformContentService"/> against a REAL <see cref="JintScriptExecutor"/> (the exact same
/// compile/validate-on-save path a tenant's own script editor uses) and, for the sandbox-parity proof, a real
/// <see cref="ScriptRunner"/> over the hardened Jint sandbox — all sharing one relational SQLite
/// <see cref="PlatformContentTestDbContext"/>, so "a platform-published script runs under the same sandbox
/// limits as a tenant script" is proven by really running it, never inferred from a row value.
/// </summary>
public sealed class PlatformContentServiceCodeScriptTests : IAsyncDisposable
{
    private readonly PlatformContentTestDbContext _db = PlatformContentTestDbContext.New();
    private readonly IPlatformIamService _iam = Substitute.For<IPlatformIamService>();
    private readonly JintScriptExecutor _executor = new();
    private readonly Guid _actingPrincipalId = Guid.NewGuid();

    public PlatformContentServiceCodeScriptTests()
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

    private PlatformContentService CreateService() =>
        new(
            _db,
            _iam,
            new TestUnitOfWork(_db),
            Substitute.For<IVueSfcCompiler>(),
            Substitute.For<IWidgetService>(),
            Substitute.For<Application.Commands.Services.IPipelineService>(),
            _executor
        );

    private static string CodeScriptPayload(string sourceCode) =>
        JsonSerializer.Serialize(new { sourceCode });

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

    private async Task<(
        PlatformContentDefinition Definition,
        PlatformContentVersion V1
    )> SeedPublishedCodeScriptDefinitionAsync(string key = "welcome_script")
    {
        string payload = CodeScriptPayload("bot.send('v1');");
        PlatformContentDefinition definition = new()
        {
            Kind = PlatformContentKinds.CodeScript,
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

    /// <summary>Seeds a tenant <see cref="CodeScript"/> the way an install-from-platform-content would leave
    /// it: a real compiled version (through the SAME <see cref="JintScriptExecutor"/> the fan-out uses) with
    /// provenance stamped exactly as a successful publish would stamp it — the row this test starts from is
    /// exactly the shape a real installed script is in before its NEXT publish.</summary>
    private async Task<CodeScript> SeedInstalledCodeScriptAsync(
        Guid broadcasterId,
        PlatformContentDefinition definition,
        PlatformContentVersion installedVersion,
        string sourceCode,
        string name = "installed-script"
    )
    {
        Result<ScriptCompilation> compiled = await _executor.CompileAsync(sourceCode);
        Assert.True(compiled.IsSuccess, compiled.ErrorMessage);

        DateTime now = DateTime.UtcNow;
        CodeScript script = new()
        {
            BroadcasterId = broadcasterId,
            Name = name,
            Language = "typescript",
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.CodeScripts.Add(script);

        CodeScriptVersion version = new()
        {
            CodeScriptId = script.Id,
            BroadcasterId = broadcasterId,
            Version = 1,
            SourceCode = sourceCode,
            CompiledJs = compiled.Value.CompiledJs,
            CompiledHash = compiled.Value.CompiledHash,
            ValidationStatus = "valid",
            DeclaredCapabilitiesJson = JsonSerializer.Serialize(
                compiled.Value.DeclaredCapabilities
            ),
            PublishedAt = now,
        };
        _db.CodeScriptVersions.Add(version);
        script.CurrentVersionId = version.Id;

        script.PlatformSourceDefinitionId = definition.Id;
        script.PlatformSourceVersion = installedVersion.Version;
        script.PlatformSourceHash = CodeScriptContentPayload.ComputeSourceHash(sourceCode);
        script.PlatformSourceSyncedAt = now;

        await _db.SaveChangesAsync();
        return script;
    }

    /// <summary>Simulates a tenant editing their own copy through their own editor save: appends a NEW
    /// <see cref="CodeScriptVersion"/> with different source and hot-swaps <c>CurrentVersionId</c> to it —
    /// never touching <c>PlatformSource*</c> provenance, exactly like a real tenant save never would.</summary>
    private async Task CustomizeInstalledCodeScriptAsync(CodeScript script, string newSourceCode)
    {
        Result<ScriptCompilation> compiled = await _executor.CompileAsync(newSourceCode);
        Assert.True(compiled.IsSuccess, compiled.ErrorMessage);

        CodeScriptVersion version = new()
        {
            CodeScriptId = script.Id,
            BroadcasterId = script.BroadcasterId,
            Version = 2,
            SourceCode = newSourceCode,
            CompiledJs = compiled.Value.CompiledJs,
            CompiledHash = compiled.Value.CompiledHash,
            ValidationStatus = "valid",
            DeclaredCapabilitiesJson = JsonSerializer.Serialize(
                compiled.Value.DeclaredCapabilities
            ),
            PublishedAt = DateTime.UtcNow,
        };
        _db.CodeScriptVersions.Add(version);
        script.CurrentVersionId = version.Id;
        await _db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------------------------------------------
    // DONE-WHEN 1: the blast-radius preview returns the REAL counted number of affected tenants — seed
    // exactly 3, assert exactly 3 — and a STALE preview count fails closed rather than silently publishing.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task PreviewPublish_SeedingThreeUntouchedTenants_ReturnsExactlyThree_AndStalePreviewFailsClosed()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedCodeScriptDefinitionAsync();

        await SeedInstalledCodeScriptAsync(
            (await AddChannelAsync("tenant-1")).Id,
            definition,
            v1,
            "bot.send('v1');"
        );
        await SeedInstalledCodeScriptAsync(
            (await AddChannelAsync("tenant-2")).Id,
            definition,
            v1,
            "bot.send('v1');"
        );
        await SeedInstalledCodeScriptAsync(
            (await AddChannelAsync("tenant-3")).Id,
            definition,
            v1,
            "bot.send('v1');"
        );

        PlatformContentVersion v2 = await DraftVersionAsync(
            definition,
            2,
            CodeScriptPayload("bot.send('v2');")
        );

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

        // A stale confirmed count must fail closed rather than silently publishing to a different set of
        // tenants than what was shown.
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
    // DONE-WHEN 2: the seeder law's three real cases, asserted as per-tenant STATE after propagation runs.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Publish_UntouchedTenantReceivesUpdate_CustomizedTenantIsNotOverwritten_DeletedTenantIsNotResurrected()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedCodeScriptDefinitionAsync();

        Channel untouchedChannel = await AddChannelAsync("untouched-streamer");
        CodeScript untouchedScript = await SeedInstalledCodeScriptAsync(
            untouchedChannel.Id,
            definition,
            v1,
            "bot.send('v1');"
        );

        Channel customizedChannel = await AddChannelAsync("customized-streamer");
        CodeScript customizedScript = await SeedInstalledCodeScriptAsync(
            customizedChannel.Id,
            definition,
            v1,
            "bot.send('v1');"
        );
        await CustomizeInstalledCodeScriptAsync(
            customizedScript,
            "bot.send('customized-by-streamer');"
        );

        Channel deletedChannel = await AddChannelAsync("deleted-streamer");
        CodeScript deletedScript = await SeedInstalledCodeScriptAsync(
            deletedChannel.Id,
            definition,
            v1,
            "bot.send('v1');"
        );
        string deletedScriptSourceBeforeDelete = "bot.send('v1');";
        deletedScript.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        PlatformContentVersion v2 = await DraftVersionAsync(
            definition,
            2,
            CodeScriptPayload("bot.send('v2');")
        );

        PlatformContentService sut = CreateService();

        // Preview must count exactly the untouched tenant — never the customized or deleted ones.
        Result<PublishPreviewDto> preview = await sut.PreviewPublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            PlatformContentPublishModes.UpdateInPlaceWhereUntouched
        );
        Assert.True(preview.IsSuccess, preview.ErrorMessage);
        Assert.Equal(1, preview.Value.AffectedCount);
        Assert.Equal(1, preview.Value.SkippedCount); // the customized tenant; the deleted one is excluded outright

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
        Assert.Equal(1, publishResult.Value.ConfirmedAffectedCount);
        Assert.Empty(publishResult.Value.ValidationFailedCodeScriptIds);

        // Case 1: the untouched tenant's copy now reflects the new platform version.
        CodeScript untouchedAfter = await _db
            .CodeScripts.AsNoTracking()
            .SingleAsync(s => s.Id == untouchedScript.Id);
        Assert.Equal(2, untouchedAfter.PlatformSourceVersion);
        Assert.Equal(
            CodeScriptContentPayload.ComputeSourceHash("bot.send('v2');"),
            untouchedAfter.PlatformSourceHash
        );
        CodeScriptVersion untouchedCurrentVersion = await _db
            .CodeScriptVersions.AsNoTracking()
            .SingleAsync(v => v.Id == untouchedAfter.CurrentVersionId);
        Assert.Equal("bot.send('v2');", untouchedCurrentVersion.SourceCode);
        Assert.Equal(2, untouchedCurrentVersion.Version);

        // Case 2: the customized tenant keeps their own edit — never silently overwritten.
        CodeScript customizedAfter = await _db
            .CodeScripts.AsNoTracking()
            .SingleAsync(s => s.Id == customizedScript.Id);
        Assert.Equal(1, customizedAfter.PlatformSourceVersion); // still on v1 provenance
        CodeScriptVersion customizedCurrentVersion = await _db
            .CodeScriptVersions.AsNoTracking()
            .SingleAsync(v => v.Id == customizedAfter.CurrentVersionId);
        Assert.Equal("bot.send('customized-by-streamer');", customizedCurrentVersion.SourceCode);

        // Case 3: the deleted tenant's row is never resurrected — still deleted, never touched.
        CodeScript deletedAfter = await _db
            .CodeScripts.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(s => s.Id == deletedScript.Id);
        Assert.NotNull(deletedAfter.DeletedAt);
        Assert.Equal(1, deletedAfter.PlatformSourceVersion);
        CodeScriptVersion deletedCurrentVersion = await _db
            .CodeScriptVersions.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(v => v.Id == deletedAfter.CurrentVersionId);
        Assert.Equal(deletedScriptSourceBeforeDelete, deletedCurrentVersion.SourceCode);
    }

    // ---------------------------------------------------------------------------------------------------
    // DONE-WHEN 3: a platform-published script executes under the SAME sandbox constraint as a
    // tenant-authored script. The Jint sandbox enforces a hard statement-count ceiling
    // (JintEngineFactory.CreateHardened -> options.MaxStatements(200_000), ScriptResourceBudget.Baseline) —
    // an infinite loop overflows it deterministically (no timing flake) and JintScriptExecutor maps that to
    // Faulted/"Execution exceeded a resource limit." for ANY script, regardless of provenance. This proves
    // there is no separate, wider-powered execution path for platform-published content.
    // ---------------------------------------------------------------------------------------------------

    private const string InfiniteLoopSource = "var i = 0; while (true) { i++; }";

    private static ScriptRunner CreateRunner(
        PlatformContentTestDbContext db,
        JintScriptExecutor executor
    )
    {
        IScriptCapabilityBroker broker = Substitute.For<IScriptCapabilityBroker>();
        broker
            .BuildGrantAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => Result.Success(new ScriptCapabilityGrant(ci.ArgAt<Guid>(0), [])));
        IScriptExecutionMeter meter = Substitute.For<IScriptExecutionMeter>();
        meter
            .CheckSandboxBudgetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new QuotaCheck(true, -1, 0, default, default)));
        meter
            .RecordSandboxUsageAsync(
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        ScriptHostBridgeFactory bridgeFactory = new(
            Substitute.For<NomNomzBot.Domain.Chat.Interfaces.IChatProvider>(),
            Substitute.For<Application.Economy.Services.ICurrencyAccountService>(),
            Substitute.For<Application.Music.Services.IMusicService>(),
            Substitute.For<IHttpClientFactory>(),
            Substitute.For<IScriptStorageService>(),
            Substitute.For<Application.Contracts.Tts.ITtsDispatchService>(),
            Substitute.For<Application.Widgets.Services.IWidgetService>(),
            Substitute.For<Application.Widgets.Services.IWidgetEventNotifier>(),
            Substitute.For<Application.Rewards.Services.IRewardService>(),
            Substitute.For<Application.Contracts.Analytics.IViewerAnalyticsService>(),
            Substitute.For<Application.Tts.Services.ITtsConfigService>(),
            Substitute.For<Application.Commands.Services.IScheduledPipelineService>(),
            db,
            Substitute.For<Application.Chat.Services.ISevenTvUserPaintResolver>()
        );
        return new(
            db,
            executor,
            broker,
            meter,
            bridgeFactory,
            new FakeTimeProvider(DateTimeOffset.UtcNow)
        );
    }

    [Fact]
    public async Task Publish_PlatformPublishedScript_RunsUnderTheSameSandboxLimit_AsATenantAuthoredScript()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedCodeScriptDefinitionAsync();

        Channel channel = await AddChannelAsync("streamer");
        CodeScript installed = await SeedInstalledCodeScriptAsync(
            channel.Id,
            definition,
            v1,
            "bot.send('v1');"
        );

        PlatformContentVersion runaway = await DraftVersionAsync(
            definition,
            2,
            CodeScriptPayload(InfiniteLoopSource)
        );

        PlatformContentService sut = CreateService();
        Result<PlatformContentPublishJobDto> publishResult = await sut.PublishAsync(
            _actingPrincipalId,
            definition.Id,
            runaway.Id,
            new PublishContentRequest(
                PlatformContentPublishModes.UpdateInPlaceWhereUntouched,
                PublishNote: null,
                ConfirmedPreviewAffectedCount: 1
            )
        );
        Assert.True(publishResult.IsSuccess, publishResult.ErrorMessage);
        Assert.Equal(1, publishResult.Value.ConfirmedAffectedCount);

        ScriptRunner runner = CreateRunner(_db, _executor);

        Result<ScriptRunResult> platformScriptRun = await runner.RunAsync(
            installed.Id,
            new ScriptInvocation(
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                "tester",
                [],
                new Dictionary<string, string>()
            )
        );
        Assert.True(platformScriptRun.IsSuccess, platformScriptRun.ErrorMessage);
        Assert.Equal(ScriptExecutionOutcome.Faulted, platformScriptRun.Value.Outcome);
        Assert.Equal("Execution exceeded a resource limit.", platformScriptRun.Value.ErrorMessage);

        // Comparative proof: a tenant-authored script (never touched by the platform-content spine) with the
        // EXACT SAME source hits the EXACT SAME sandbox wall through the SAME runner/executor — there is no
        // widened budget or bypassed limit reserved for platform-published content.
        Channel tenantOnlyChannel = await AddChannelAsync("tenant-authored-streamer");
        Result<ScriptCompilation> tenantCompiled = await _executor.CompileAsync(InfiniteLoopSource);
        Assert.True(tenantCompiled.IsSuccess, tenantCompiled.ErrorMessage);
        DateTime now = DateTime.UtcNow;
        CodeScript tenantScript = new()
        {
            BroadcasterId = tenantOnlyChannel.Id,
            Name = "tenant-authored",
            Language = "typescript",
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.CodeScripts.Add(tenantScript);
        CodeScriptVersion tenantVersion = new()
        {
            CodeScriptId = tenantScript.Id,
            BroadcasterId = tenantOnlyChannel.Id,
            Version = 1,
            SourceCode = InfiniteLoopSource,
            CompiledJs = tenantCompiled.Value.CompiledJs,
            CompiledHash = tenantCompiled.Value.CompiledHash,
            ValidationStatus = "valid",
            PublishedAt = now,
        };
        _db.CodeScriptVersions.Add(tenantVersion);
        tenantScript.CurrentVersionId = tenantVersion.Id;
        await _db.SaveChangesAsync();

        Result<ScriptRunResult> tenantScriptRun = await runner.RunAsync(
            tenantScript.Id,
            new ScriptInvocation(
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                "tester",
                [],
                new Dictionary<string, string>()
            )
        );
        Assert.True(tenantScriptRun.IsSuccess, tenantScriptRun.ErrorMessage);
        Assert.Equal(platformScriptRun.Value.Outcome, tenantScriptRun.Value.Outcome);
        Assert.Equal(platformScriptRun.Value.ErrorMessage, tenantScriptRun.Value.ErrorMessage);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();
}
