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
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.PlatformContent.Entities;
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
    private readonly Guid _actingPrincipalId = Guid.NewGuid();

    private PlatformContentService CreateService() => new(_db, _iam, new TestUnitOfWork(_db));

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

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();
}
