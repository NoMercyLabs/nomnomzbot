// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Commands;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// S008c-read-a: proves the read side of H.4 PipelineExecution (persisted by S008b, 75519b88) actually
/// surfaces per-channel run history — tenant isolation, real newest-first pagination (not the systemic
/// "capped at 25" bug class), and the failing-step detail a streamer needs to debug a misbehaving pipeline.
/// </summary>
public class PipelineExecutionQueryServiceTests
{
    private static PipelineExecution BuildExecution(
        Guid broadcasterId,
        Guid pipelineId,
        string status,
        DateTime startedAt,
        string? stepLogsJson = null,
        string? errorMessage = null
    ) =>
        new()
        {
            PipelineId = pipelineId,
            BroadcasterId = broadcasterId,
            TriggerKind = "pipeline",
            Status = status,
            HostCallCount = 2,
            DurationMs = 120,
            ErrorMessage = errorMessage,
            StepLogsJson = stepLogsJson,
            StartedAt = startedAt,
            CompletedAt = startedAt.AddMilliseconds(120),
        };

    [Fact]
    public async Task ListAsync_OnlyReturnsCallingTenantsRows_NewestFirst_CorrectlyPagedAcrossPages()
    {
        PipelineExecutionQueryTestDbContext db = PipelineExecutionQueryTestDbContext.New();
        IPipelineExecutionQueryService service = new PipelineExecutionQueryService(db);

        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        Guid pipelineA = Guid.NewGuid();
        Guid pipelineB = Guid.NewGuid();
        DateTime baseTime = new(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);

        // Tenant A: three runs, oldest to newest.
        PipelineExecution aOldest = BuildExecution(
            tenantA,
            pipelineA,
            "completed",
            baseTime.AddMinutes(1)
        );
        PipelineExecution aMiddle = BuildExecution(
            tenantA,
            pipelineA,
            "completed",
            baseTime.AddMinutes(2)
        );
        PipelineExecution aNewest = BuildExecution(
            tenantA,
            pipelineA,
            "completed",
            baseTime.AddMinutes(3)
        );

        // Tenant B: a run that must never leak into tenant A's results.
        PipelineExecution bRun = BuildExecution(
            tenantB,
            pipelineB,
            "completed",
            baseTime.AddMinutes(10)
        );

        db.PipelineExecutions.AddRange(aOldest, aMiddle, aNewest, bRun);
        await db.SaveChangesAsync();

        Result<PagedList<PipelineExecutionSummaryDto>> page1Result = await service.ListAsync(
            tenantA.ToString(),
            new(Page: 1, PageSize: 1),
            failuresOnly: false
        );
        Result<PagedList<PipelineExecutionSummaryDto>> page2Result = await service.ListAsync(
            tenantA.ToString(),
            new(Page: 2, PageSize: 1),
            failuresOnly: false
        );
        Result<PagedList<PipelineExecutionSummaryDto>> page3Result = await service.ListAsync(
            tenantA.ToString(),
            new(Page: 3, PageSize: 1),
            failuresOnly: false
        );

        Assert.True(page1Result.IsSuccess);
        Assert.True(page2Result.IsSuccess);
        Assert.True(page3Result.IsSuccess);

        PagedList<PipelineExecutionSummaryDto> page1 = page1Result.Value;
        PagedList<PipelineExecutionSummaryDto> page2 = page2Result.Value;
        PagedList<PipelineExecutionSummaryDto> page3 = page3Result.Value;

        // Total count must reflect tenant A only (3), never tenant B's row leaking in.
        Assert.Equal(3, page1.TotalCount);

        // Newest-first: page 1 = newest, page 2 = middle, page 3 = oldest. Distinct ids per page.
        Assert.Equal(aNewest.Id, Assert.Single(page1.Items).Id);
        Assert.Equal(aMiddle.Id, Assert.Single(page2.Items).Id);
        Assert.Equal(aOldest.Id, Assert.Single(page3.Items).Id);

        // Tenant B's row never appears across any page for tenant A.
        Assert.DoesNotContain(
            page1.Items.Concat(page2.Items).Concat(page3.Items),
            i => i.Id == bRun.Id
        );
    }

    [Fact]
    public async Task GetDetailAsync_ReturnsFailingStepIdentityAndError_AndFailuresOnlyFilterExcludesSuccess()
    {
        PipelineExecutionQueryTestDbContext db = PipelineExecutionQueryTestDbContext.New();
        IPipelineExecutionQueryService service = new PipelineExecutionQueryService(db);

        Guid tenant = Guid.NewGuid();
        Guid pipelineId = Guid.NewGuid();
        DateTime baseTime = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

        const string failingStepAction = "http_request";
        const string failingStepError = "Timeout calling webhook";
        string stepLogsJson = $$"""
            [
                {"StepIndex":0,"ActionType":"send_message","Succeeded":true,"DurationMs":10,"ErrorMessage":null},
                {"StepIndex":1,"ActionType":"{{failingStepAction}}","Succeeded":false,"DurationMs":50,"ErrorMessage":"{{failingStepError}}"}
            ]
            """;

        PipelineExecution partiallyFailed = BuildExecution(
            tenant,
            pipelineId,
            "partially_failed",
            baseTime,
            stepLogsJson,
            errorMessage: failingStepError
        );
        PipelineExecution succeeded = BuildExecution(
            tenant,
            pipelineId,
            "completed",
            baseTime.AddMinutes(1)
        );

        db.PipelineExecutions.AddRange(partiallyFailed, succeeded);
        await db.SaveChangesAsync();

        // Detail read identifies the failing step.
        Result<PipelineExecutionDetailDto> detailResult = await service.GetDetailAsync(
            tenant.ToString(),
            partiallyFailed.Id
        );
        Assert.True(detailResult.IsSuccess);
        PipelineExecutionDetailDto detail = detailResult.Value;
        Assert.Equal(2, detail.StepLogs.Count);
        PipelineExecutionStepLogDto failingStep = Assert.Single(detail.StepLogs, s => !s.Succeeded);
        Assert.Equal(1, failingStep.StepIndex);
        Assert.Equal(failingStepAction, failingStep.ActionType);
        Assert.Equal(failingStepError, failingStep.ErrorMessage);

        // failuresOnly filter: returns the partially-failed run, excludes the fully-successful one.
        Result<PagedList<PipelineExecutionSummaryDto>> filteredResult = await service.ListAsync(
            tenant.ToString(),
            new(Page: 1, PageSize: 25),
            failuresOnly: true
        );
        Assert.True(filteredResult.IsSuccess);
        PipelineExecutionSummaryDto onlyResult = Assert.Single(filteredResult.Value.Items);
        Assert.Equal(partiallyFailed.Id, onlyResult.Id);
        Assert.DoesNotContain(filteredResult.Value.Items, i => i.Id == succeeded.Id);
    }
}
