// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Content.PlatformContent;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Content.PlatformContent;

/// <summary>
/// S-ADMIN-2a — proves the platform-content propagation engine (platform-admin.md §2.1, §5) against a real
/// relational SQLite database: publish modes change the right tenant rows and no others, the blast-radius
/// preview matches real rows before anything is written, and every publish appends the audit contract.
/// </summary>
public sealed class PlatformContentServiceTests : IAsyncDisposable
{
    private readonly PlatformContentTestDbContext _db = PlatformContentTestDbContext.New();
    private readonly IPlatformIamService _iam = Substitute.For<IPlatformIamService>();
    private readonly IVueSfcCompiler _vueCompiler = Substitute.For<IVueSfcCompiler>();
    private readonly Guid _actingPrincipalId = Guid.NewGuid();

    private PlatformContentService CreateService() =>
        new(_db, _iam, new TestUnitOfWork(_db), _vueCompiler);

    public PlatformContentServiceTests()
    {
        // The service re-asserts every action through IPlatformIamService.AuthorizePlatformAsync — that
        // funnel's own allow/deny/audit behaviour is PlatformIamService's own test suite's job; here it is
        // stubbed to allow, so these tests isolate PlatformContentService's own propagation logic and its
        // OWN audit row (AuditPublishOutcomeAsync), never PlatformIamService's internal audit write.
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

        // Widget-kind publishes gate on a real compile; default to a clean compile so command-kind tests and
        // widget-kind "happy path" tests never need to stub this explicitly — only the compile-rejection test
        // overrides it.
        _vueCompiler
            .Compile(Arg.Any<string>(), Arg.Any<string>())
            .Returns(Result.Success(new VueSfcOutput("export default {}", "")));
    }

    private async Task<Widget> AddWidgetAsync(
        Guid broadcasterId,
        Dictionary<string, object> settings,
        List<string> eventSubscriptions,
        Guid? sourceDefinitionId,
        int? sourceVersion,
        string? sourceHash
    )
    {
        Widget row = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcasterId,
            Name = "now-playing",
            Framework = "vue",
            Source = "first_party",
            Settings = settings,
            EventSubscriptions = eventSubscriptions,
            PlatformSourceDefinitionId = sourceDefinitionId,
            PlatformSourceVersion = sourceVersion,
            PlatformSourceHash = sourceHash,
            PlatformSourceSyncedAt = sourceDefinitionId is null ? null : DateTime.UtcNow,
        };
        _db.Widgets.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private async Task<(
        PlatformContentDefinition Definition,
        PlatformContentVersion V1
    )> SeedPublishedWidgetDefinitionAsync(string key = "now-playing")
    {
        const string payload =
            "{\"sourceCode\":\"<template><div/></template>\",\"defaultSettings\":{\"color\":\"red\"},\"defaultEventSubscriptions\":[\"music.now_playing\"]}";
        PlatformContentDefinition definition = new()
        {
            Kind = PlatformContentKinds.Widget,
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

    private async Task<ChannelBuiltinCommand> AddBuiltinAsync(
        Guid broadcasterId,
        string key,
        string? overridesJson,
        Guid? sourceDefinitionId,
        int? sourceVersion,
        string? sourceHash
    )
    {
        ChannelBuiltinCommand row = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcasterId,
            BuiltinKey = key,
            IsEnabled = true,
            OverridesJson = overridesJson,
            PlatformSourceDefinitionId = sourceDefinitionId,
            PlatformSourceVersion = sourceVersion,
            PlatformSourceHash = sourceHash,
            PlatformSourceSyncedAt = sourceDefinitionId is null ? null : DateTime.UtcNow,
        };
        _db.ChannelBuiltinCommands.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private async Task<(
        PlatformContentDefinition Definition,
        PlatformContentVersion V1
    )> SeedPublishedDefinitionAsync(string key = "sr")
    {
        PlatformContentDefinition definition = new()
        {
            Kind = PlatformContentKinds.Command,
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
            ContentHash = PlatformContentHash.ComputeHash("{}"),
            PayloadJson = "{}",
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

    // ---------------------------------------------------------------------------------------------------
    // DONE-WHEN 1 + 3: update_in_place_where_untouched changes an untouched tenant's row, leaves a
    // customised tenant's row exactly as it was.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task UpdateInPlaceWhereUntouched_ChangesUntouchedTenant_LeavesCustomisedTenantUnchanged()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedDefinitionAsync();

        Channel untouchedChannel = await AddChannelAsync("untouched-streamer");
        ChannelBuiltinCommand untouchedRow = await AddBuiltinAsync(
            untouchedChannel.Id,
            "sr",
            overridesJson: null,
            sourceDefinitionId: definition.Id,
            sourceVersion: 1,
            sourceHash: v1.ContentHash
        );

        Channel customisedChannel = await AddChannelAsync("customised-streamer");
        const string customPayload = "{\"cooldownSeconds\":30}";
        ChannelBuiltinCommand customisedRow = await AddBuiltinAsync(
            customisedChannel.Id,
            "sr",
            overridesJson: customPayload,
            sourceDefinitionId: definition.Id,
            sourceVersion: 1,
            sourceHash: v1.ContentHash
        );

        PlatformContentVersion v2 = new()
        {
            DefinitionId = definition.Id,
            Version = 2,
            ContentHash = PlatformContentHash.ComputeHash("{\"cooldownSeconds\":10}"),
            PayloadJson = "{\"cooldownSeconds\":10}",
            DraftedAt = DateTime.UtcNow,
            DraftedByPrincipalId = _actingPrincipalId,
        };
        _db.PlatformContentVersions.Add(v2);
        await _db.SaveChangesAsync();

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

        ChannelBuiltinCommand untouchedAfter = await _db
            .ChannelBuiltinCommands.AsNoTracking()
            .SingleAsync(b => b.Id == untouchedRow.Id);
        Assert.Equal("{\"cooldownSeconds\":10}", untouchedAfter.OverridesJson);
        Assert.Equal(2, untouchedAfter.PlatformSourceVersion);
        Assert.Equal(v2.ContentHash, untouchedAfter.PlatformSourceHash);

        ChannelBuiltinCommand customisedAfter = await _db
            .ChannelBuiltinCommands.AsNoTracking()
            .SingleAsync(b => b.Id == customisedRow.Id);
        Assert.Equal(customPayload, customisedAfter.OverridesJson);
        Assert.Equal(1, customisedAfter.PlatformSourceVersion);
        Assert.Equal(v1.ContentHash, customisedAfter.PlatformSourceHash);
    }

    // ---------------------------------------------------------------------------------------------------
    // DONE-WHEN 2: the blast-radius preview returns correct counts from real tenant rows before anything
    // is written.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task PreviewPublish_ReturnsRealCounts_AndWritesNothing()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedDefinitionAsync();

        Channel untouched1 = await AddChannelAsync("untouched-1");
        await AddBuiltinAsync(untouched1.Id, "sr", null, definition.Id, 1, v1.ContentHash);
        Channel untouched2 = await AddChannelAsync("untouched-2");
        await AddBuiltinAsync(untouched2.Id, "sr", null, definition.Id, 1, v1.ContentHash);
        Channel customised = await AddChannelAsync("customised");
        await AddBuiltinAsync(customised.Id, "sr", "{\"x\":1}", definition.Id, 1, v1.ContentHash);
        Channel neverInstalled = await AddChannelAsync("never-installed");
        await AddBuiltinAsync(neverInstalled.Id, "sr", null, sourceDefinitionId: null, null, null);

        PlatformContentVersion v2 = new()
        {
            DefinitionId = definition.Id,
            Version = 2,
            ContentHash = PlatformContentHash.ComputeHash("{\"a\":1}"),
            PayloadJson = "{\"a\":1}",
            DraftedAt = DateTime.UtcNow,
            DraftedByPrincipalId = _actingPrincipalId,
        };
        _db.PlatformContentVersions.Add(v2);
        await _db.SaveChangesAsync();

        PlatformContentService sut = CreateService();

        Result<PublishPreviewDto> preview = await sut.PreviewPublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            PlatformContentPublishModes.UpdateInPlaceWhereUntouched
        );

        Assert.True(preview.IsSuccess, preview.ErrorMessage);
        Assert.Equal(2, preview.Value.AffectedCount);
        Assert.Equal(2, preview.Value.SkippedCount); // customised + never-installed

        // Nothing written: the four rows still hold their original OverridesJson, and no publish job exists.
        List<ChannelBuiltinCommand> rows = await _db
            .ChannelBuiltinCommands.AsNoTracking()
            .ToListAsync();
        Assert.All(rows, r => Assert.True(r.PlatformSourceVersion is null or 1));
        Assert.Equal(0, await _db.PlatformContentPublishJobs.CountAsync());
    }

    // ---------------------------------------------------------------------------------------------------
    // DONE-WHEN 3: publish_as_new touches nothing; force overwrites every installed row including edits.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task PublishAsNew_TouchesNoTenantRows()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedDefinitionAsync();

        Channel channel = await AddChannelAsync("streamer");
        const string original = "{\"keep\":true}";
        ChannelBuiltinCommand row = await AddBuiltinAsync(
            channel.Id,
            "sr",
            original,
            definition.Id,
            1,
            v1.ContentHash
        );

        PlatformContentVersion v2 = new()
        {
            DefinitionId = definition.Id,
            Version = 2,
            ContentHash = PlatformContentHash.ComputeHash("{\"new\":true}"),
            PayloadJson = "{\"new\":true}",
            DraftedAt = DateTime.UtcNow,
            DraftedByPrincipalId = _actingPrincipalId,
        };
        _db.PlatformContentVersions.Add(v2);
        await _db.SaveChangesAsync();

        PlatformContentService sut = CreateService();

        Result<PublishPreviewDto> preview = await sut.PreviewPublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            PlatformContentPublishModes.PublishAsNew
        );
        Assert.Equal(0, preview.Value.AffectedCount);

        Result<PlatformContentPublishJobDto> publishResult = await sut.PublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            new PublishContentRequest(PlatformContentPublishModes.PublishAsNew, null, 0)
        );

        Assert.True(publishResult.IsSuccess, publishResult.ErrorMessage);
        Assert.Equal(0, publishResult.Value.ConfirmedAffectedCount);

        ChannelBuiltinCommand after = await _db
            .ChannelBuiltinCommands.AsNoTracking()
            .SingleAsync(b => b.Id == row.Id);
        Assert.Equal(original, after.OverridesJson);
        Assert.Equal(1, after.PlatformSourceVersion);
    }

    [Fact]
    public async Task Force_OverwritesEveryInstalledRow_IncludingCustomisedOnes()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedDefinitionAsync();

        Channel untouchedChannel = await AddChannelAsync("untouched");
        ChannelBuiltinCommand untouchedRow = await AddBuiltinAsync(
            untouchedChannel.Id,
            "sr",
            null,
            definition.Id,
            1,
            v1.ContentHash
        );
        Channel customisedChannel = await AddChannelAsync("customised");
        ChannelBuiltinCommand customisedRow = await AddBuiltinAsync(
            customisedChannel.Id,
            "sr",
            "{\"mine\":true}",
            definition.Id,
            1,
            v1.ContentHash
        );

        PlatformContentVersion v2 = new()
        {
            DefinitionId = definition.Id,
            Version = 2,
            ContentHash = PlatformContentHash.ComputeHash("{\"forced\":true}"),
            PayloadJson = "{\"forced\":true}",
            DraftedAt = DateTime.UtcNow,
            DraftedByPrincipalId = _actingPrincipalId,
        };
        _db.PlatformContentVersions.Add(v2);
        await _db.SaveChangesAsync();

        PlatformContentService sut = CreateService();

        Result<PublishPreviewDto> preview = await sut.PreviewPublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            PlatformContentPublishModes.Force
        );
        Assert.Equal(2, preview.Value.AffectedCount);
        Assert.Equal(0, preview.Value.SkippedCount);

        Result<PlatformContentPublishJobDto> publishResult = await sut.PublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            new PublishContentRequest(
                PlatformContentPublishModes.Force,
                PublishNote: "security fix — overwrite everyone",
                ConfirmedPreviewAffectedCount: 2
            )
        );

        Assert.True(publishResult.IsSuccess, publishResult.ErrorMessage);
        Assert.Equal(2, publishResult.Value.ConfirmedAffectedCount);

        ChannelBuiltinCommand untouchedAfter = await _db
            .ChannelBuiltinCommands.AsNoTracking()
            .SingleAsync(b => b.Id == untouchedRow.Id);
        Assert.Equal("{\"forced\":true}", untouchedAfter.OverridesJson);

        ChannelBuiltinCommand customisedAfter = await _db
            .ChannelBuiltinCommands.AsNoTracking()
            .SingleAsync(b => b.Id == customisedRow.Id);
        Assert.Equal("{\"forced\":true}", customisedAfter.OverridesJson);
        Assert.Equal(2, customisedAfter.PlatformSourceVersion);
    }

    // ---------------------------------------------------------------------------------------------------
    // DONE-WHEN 4: every publish writes the audit record §5 specifies.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Publish_WritesAuditRecordWithAffectedCountAndJobId()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedDefinitionAsync();

        Channel channel = await AddChannelAsync("streamer");
        await AddBuiltinAsync(channel.Id, "sr", null, definition.Id, 1, v1.ContentHash);

        PlatformContentVersion v2 = new()
        {
            DefinitionId = definition.Id,
            Version = 2,
            ContentHash = PlatformContentHash.ComputeHash("{\"a\":1}"),
            PayloadJson = "{\"a\":1}",
            DraftedAt = DateTime.UtcNow,
            DraftedByPrincipalId = _actingPrincipalId,
        };
        _db.PlatformContentVersions.Add(v2);
        await _db.SaveChangesAsync();

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
        Assert.Equal($"command:sr@v2", auditRow.TargetResource);
        Assert.Equal(1, auditRow.AffectedTenantCount);
        Assert.Equal(IamOutcome.Allowed, auditRow.Outcome);
        Assert.Equal(_actingPrincipalId, auditRow.PrincipalId);
    }

    [Fact]
    public async Task ForcePublish_WithoutJustification_IsRejected()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedDefinitionAsync();

        PlatformContentVersion v2 = new()
        {
            DefinitionId = definition.Id,
            Version = 2,
            ContentHash = PlatformContentHash.ComputeHash("{\"a\":1}"),
            PayloadJson = "{\"a\":1}",
            DraftedAt = DateTime.UtcNow,
            DraftedByPrincipalId = _actingPrincipalId,
        };
        _db.PlatformContentVersions.Add(v2);
        await _db.SaveChangesAsync();

        PlatformContentService sut = CreateService();

        Result<PlatformContentPublishJobDto> publishResult = await sut.PublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            new PublishContentRequest(PlatformContentPublishModes.Force, PublishNote: null, 0)
        );

        Assert.True(publishResult.IsFailure);
        Assert.Equal("VALIDATION_FAILED", publishResult.ErrorCode);
        Assert.Equal(0, await _db.PlatformContentPublishJobs.CountAsync());
    }

    [Fact]
    public async Task Publish_WithStalePreviewCount_FailsClosedAsPreviewStale()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedDefinitionAsync();

        Channel channel = await AddChannelAsync("streamer");
        await AddBuiltinAsync(channel.Id, "sr", null, definition.Id, 1, v1.ContentHash);

        PlatformContentVersion v2 = new()
        {
            DefinitionId = definition.Id,
            Version = 2,
            ContentHash = PlatformContentHash.ComputeHash("{\"a\":1}"),
            PayloadJson = "{\"a\":1}",
            DraftedAt = DateTime.UtcNow,
            DraftedByPrincipalId = _actingPrincipalId,
        };
        _db.PlatformContentVersions.Add(v2);
        await _db.SaveChangesAsync();

        PlatformContentService sut = CreateService();

        // Confirmed count (5) does not match the real affected count (1) — a tenant edited/onboarded
        // between preview and publish.
        Result<PlatformContentPublishJobDto> publishResult = await sut.PublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            new PublishContentRequest(
                PlatformContentPublishModes.UpdateInPlaceWhereUntouched,
                null,
                5
            )
        );

        Assert.True(publishResult.IsFailure);
        Assert.Equal("PREVIEW_STALE", publishResult.ErrorCode);
        Assert.Equal(0, await _db.PlatformContentPublishJobs.CountAsync());
    }

    // ---------------------------------------------------------------------------------------------------
    // S-ADMIN-2c — widget kind on the same spine (platform-admin.md §3.1, generalized to Widget).
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Widget_UpdateInPlaceWhereUntouched_ChangesUntouchedTenant_LeavesCustomisedTenantUnchanged()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedWidgetDefinitionAsync();
        string v1SettingsHash = WidgetContentPayload.ComputeSettingsHash(
            new Dictionary<string, object> { ["color"] = "red" },
            ["music.now_playing"]
        );

        Channel untouchedChannel = await AddChannelAsync("untouched-streamer");
        Widget untouchedRow = await AddWidgetAsync(
            untouchedChannel.Id,
            new Dictionary<string, object> { ["color"] = "red" },
            ["music.now_playing"],
            definition.Id,
            1,
            v1SettingsHash
        );

        Channel customisedChannel = await AddChannelAsync("customised-streamer");
        Widget customisedRow = await AddWidgetAsync(
            customisedChannel.Id,
            new Dictionary<string, object> { ["color"] = "blue" }, // tenant edited the colour
            ["music.now_playing"],
            definition.Id,
            1,
            v1SettingsHash
        );

        const string v2Payload =
            "{\"sourceCode\":\"<template><div/></template>\",\"defaultSettings\":{\"color\":\"green\"},\"defaultEventSubscriptions\":[\"music.now_playing\"]}";
        PlatformContentVersion v2 = new()
        {
            DefinitionId = definition.Id,
            Version = 2,
            ContentHash = PlatformContentHash.ComputeHash(v2Payload),
            PayloadJson = v2Payload,
            DraftedAt = DateTime.UtcNow,
            DraftedByPrincipalId = _actingPrincipalId,
        };
        _db.PlatformContentVersions.Add(v2);
        await _db.SaveChangesAsync();

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
        Assert.Equal(1, publishResult.Value.ConfirmedAffectedCount);

        Widget untouchedAfter = await _db
            .Widgets.AsNoTracking()
            .SingleAsync(w => w.Id == untouchedRow.Id);
        Assert.Equal("green", untouchedAfter.Settings["color"]);
        Assert.Equal(2, untouchedAfter.PlatformSourceVersion);

        Widget customisedAfter = await _db
            .Widgets.AsNoTracking()
            .SingleAsync(w => w.Id == customisedRow.Id);
        Assert.Equal("blue", customisedAfter.Settings["color"]);
        Assert.Equal(1, customisedAfter.PlatformSourceVersion);
    }

    [Fact]
    public async Task Widget_PreviewPublish_ReturnsRealCounts_AndWritesNothing()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedWidgetDefinitionAsync();
        string v1SettingsHash = WidgetContentPayload.ComputeSettingsHash(
            new Dictionary<string, object> { ["color"] = "red" },
            ["music.now_playing"]
        );

        Channel untouched1 = await AddChannelAsync("untouched-1");
        await AddWidgetAsync(
            untouched1.Id,
            new Dictionary<string, object> { ["color"] = "red" },
            ["music.now_playing"],
            definition.Id,
            1,
            v1SettingsHash
        );
        Channel customised = await AddChannelAsync("customised");
        await AddWidgetAsync(
            customised.Id,
            new Dictionary<string, object> { ["color"] = "purple" },
            ["music.now_playing"],
            definition.Id,
            1,
            v1SettingsHash
        );
        Channel neverInstalled = await AddChannelAsync("never-installed");
        await AddWidgetAsync(
            neverInstalled.Id,
            new Dictionary<string, object> { ["color"] = "red" },
            ["music.now_playing"],
            sourceDefinitionId: null,
            null,
            null
        );

        const string v2Payload =
            "{\"sourceCode\":\"<template><div/></template>\",\"defaultSettings\":{\"color\":\"green\"},\"defaultEventSubscriptions\":[\"music.now_playing\"]}";
        PlatformContentVersion v2 = new()
        {
            DefinitionId = definition.Id,
            Version = 2,
            ContentHash = PlatformContentHash.ComputeHash(v2Payload),
            PayloadJson = v2Payload,
            DraftedAt = DateTime.UtcNow,
            DraftedByPrincipalId = _actingPrincipalId,
        };
        _db.PlatformContentVersions.Add(v2);
        await _db.SaveChangesAsync();

        PlatformContentService sut = CreateService();

        Result<PublishPreviewDto> preview = await sut.PreviewPublishAsync(
            _actingPrincipalId,
            definition.Id,
            v2.Id,
            PlatformContentPublishModes.UpdateInPlaceWhereUntouched
        );

        Assert.True(preview.IsSuccess, preview.ErrorMessage);
        Assert.Equal(1, preview.Value.AffectedCount); // untouched1 only
        Assert.Equal(1, preview.Value.SkippedCount); // customised (never-installed isn't in the candidate set)

        // Nothing written: every widget row still holds its original settings, and no publish job exists.
        List<Widget> rows = await _db.Widgets.AsNoTracking().ToListAsync();
        Assert.All(rows, w => Assert.True(w.PlatformSourceVersion is null or 1));
        Assert.Equal(0, await _db.PlatformContentPublishJobs.CountAsync());
    }

    [Fact]
    public async Task Widget_Publish_RejectsWhenSourceFailsToCompile_WritesNothing()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedWidgetDefinitionAsync();
        string v1SettingsHash = WidgetContentPayload.ComputeSettingsHash(
            new Dictionary<string, object> { ["color"] = "red" },
            ["music.now_playing"]
        );
        Channel channel = await AddChannelAsync("streamer");
        Widget row = await AddWidgetAsync(
            channel.Id,
            new Dictionary<string, object> { ["color"] = "red" },
            ["music.now_playing"],
            definition.Id,
            1,
            v1SettingsHash
        );

        const string brokenPayload =
            "{\"sourceCode\":\"<template><div></template>\",\"defaultSettings\":{\"color\":\"green\"},\"defaultEventSubscriptions\":[\"music.now_playing\"]}";
        PlatformContentVersion v2 = new()
        {
            DefinitionId = definition.Id,
            Version = 2,
            ContentHash = PlatformContentHash.ComputeHash(brokenPayload),
            PayloadJson = brokenPayload,
            DraftedAt = DateTime.UtcNow,
            DraftedByPrincipalId = _actingPrincipalId,
        };
        _db.PlatformContentVersions.Add(v2);
        await _db.SaveChangesAsync();

        _vueCompiler
            .Compile(Arg.Any<string>(), Arg.Any<string>())
            .Returns(
                Result.Failure<VueSfcOutput>("Unclosed tag <div>.", "WIDGET_VUE_COMPILE_FAILED")
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

        Assert.True(publishResult.IsFailure);
        Assert.Equal("VALIDATION_FAILED", publishResult.ErrorCode);
        Assert.Equal(0, await _db.PlatformContentPublishJobs.CountAsync());

        Widget rowAfter = await _db.Widgets.AsNoTracking().SingleAsync(w => w.Id == row.Id);
        Assert.Equal("red", rowAfter.Settings["color"]);
        Assert.Equal(1, rowAfter.PlatformSourceVersion);
    }

    [Fact]
    public async Task Widget_Publish_WritesAuditRecordWithAffectedCountAndJobId()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedWidgetDefinitionAsync();
        string v1SettingsHash = WidgetContentPayload.ComputeSettingsHash(
            new Dictionary<string, object> { ["color"] = "red" },
            ["music.now_playing"]
        );
        Channel channel = await AddChannelAsync("streamer");
        await AddWidgetAsync(
            channel.Id,
            new Dictionary<string, object> { ["color"] = "red" },
            ["music.now_playing"],
            definition.Id,
            1,
            v1SettingsHash
        );

        const string v2Payload =
            "{\"sourceCode\":\"<template><div/></template>\",\"defaultSettings\":{\"color\":\"green\"},\"defaultEventSubscriptions\":[\"music.now_playing\"]}";
        PlatformContentVersion v2 = new()
        {
            DefinitionId = definition.Id,
            Version = 2,
            ContentHash = PlatformContentHash.ComputeHash(v2Payload),
            PayloadJson = v2Payload,
            DraftedAt = DateTime.UtcNow,
            DraftedByPrincipalId = _actingPrincipalId,
        };
        _db.PlatformContentVersions.Add(v2);
        await _db.SaveChangesAsync();

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
        Assert.Equal("widget:now-playing@v2", auditRow.TargetResource);
        Assert.Equal(1, auditRow.AffectedTenantCount);
        Assert.Equal(IamOutcome.Allowed, auditRow.Outcome);
    }

    // ---------------------------------------------------------------------------------------------------
    // §3.1: retiring a definition stops it being offered again and touches NOTHING that is already
    // installed. This is what makes RetireDefinition's [NotDestructive] classification true rather than
    // merely present — the scanner accepts any classification, so only this test can catch a retire that
    // grows a cascade.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RetireDefinition_StampsRetiredAt_AndLeavesEveryInstalledTenantCopyRunning()
    {
        (PlatformContentDefinition definition, PlatformContentVersion v1) =
            await SeedPublishedDefinitionAsync();

        Channel untouchedChannel = await AddChannelAsync("untouched-streamer");
        ChannelBuiltinCommand untouchedRow = await AddBuiltinAsync(
            untouchedChannel.Id,
            "sr",
            overridesJson: null,
            definition.Id,
            sourceVersion: 1,
            v1.ContentHash
        );

        Channel customisedChannel = await AddChannelAsync("customised-streamer");
        const string customPayload = "{\"cooldownSeconds\":99}";
        ChannelBuiltinCommand customisedRow = await AddBuiltinAsync(
            customisedChannel.Id,
            "sr",
            customPayload,
            definition.Id,
            sourceVersion: 1,
            sourceHash: "a-hash-that-no-longer-matches"
        );

        PlatformContentService sut = CreateService();
        Result result = await sut.RetireDefinitionAsync(_actingPrincipalId, definition.Id);

        Assert.True(result.IsSuccess, result.ErrorMessage);

        PlatformContentDefinition retired = await _db
            .PlatformContentDefinitions.AsNoTracking()
            .SingleAsync(d => d.Id == definition.Id);
        Assert.NotNull(retired.RetiredAt);

        // Both installed copies keep running, byte for byte: enabled, their own overrides, and still
        // pointing at the definition they came from. A retire that cascaded would blank one of these.
        ChannelBuiltinCommand untouchedAfter = await _db
            .ChannelBuiltinCommands.AsNoTracking()
            .SingleAsync(b => b.Id == untouchedRow.Id);
        Assert.True(untouchedAfter.IsEnabled);
        Assert.Null(untouchedAfter.OverridesJson);
        Assert.Equal(definition.Id, untouchedAfter.PlatformSourceDefinitionId);
        Assert.Equal(1, untouchedAfter.PlatformSourceVersion);
        Assert.Equal(v1.ContentHash, untouchedAfter.PlatformSourceHash);

        ChannelBuiltinCommand customisedAfter = await _db
            .ChannelBuiltinCommands.AsNoTracking()
            .SingleAsync(b => b.Id == customisedRow.Id);
        Assert.True(customisedAfter.IsEnabled);
        Assert.Equal(customPayload, customisedAfter.OverridesJson);
        Assert.Equal(definition.Id, customisedAfter.PlatformSourceDefinitionId);
        Assert.Equal(1, customisedAfter.PlatformSourceVersion);

        // The published version itself survives — retiring the catalogue entry does not unpublish what
        // tenants are already running from.
        PlatformContentVersion versionAfter = await _db
            .PlatformContentVersions.AsNoTracking()
            .SingleAsync(v => v.Id == v1.Id);
        Assert.NotNull(versionAfter.PublishedAt);
        Assert.Equal(2, await _db.ChannelBuiltinCommands.CountAsync());
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();
}
