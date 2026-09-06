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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// S-ADMIN-6b — the background job queue (built on the real <c>ScheduledPipelineTask</c> scheduling primitive,
/// not a fabricated parallel queue) and per-tenant usage, for the 2am operator console. Proves the job list
/// reflects real queue state, that a retry genuinely re-runs the job by appending a new attempt row while
/// leaving the original readable, that a non-retryable job is rejected with a reason, and that per-tenant usage
/// is computed from real usage rows with correct tenant isolation.
/// </summary>
public sealed class AdminServiceScheduledJobsAndUsageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static (AdminService Sut, AuthDbContext Db, FakeTimeProvider Clock) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        FakeTimeProvider clock = new(Now);
        ServiceCollection services = new();
        services.AddLogging();
        services.AddHealthChecks();
        ServiceProvider provider = services.BuildServiceProvider();

        ScheduledPipelineService scheduler = new(
            db,
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<ScheduledPipelineService>.Instance
        );

        AdminService sut = new(
            db,
            clock,
            provider.GetRequiredService<HealthCheckService>(),
            Substitute.For<IPlatformBotReadinessGate>(),
            Substitute.For<IOutboundWebhookDispatcher>(),
            scheduler
        );
        return (sut, db, clock);
    }

    private static Channel SeedChannel(AuthDbContext db, string login)
    {
        Guid ownerId = Guid.NewGuid();
        db.Users.Add(
            new User
            {
                Id = ownerId,
                Username = login,
                UsernameNormalized = login.ToLowerInvariant(),
                DisplayName = login,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            }
        );
        Channel channel = new()
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerId,
            Name = login,
            NameNormalized = login.ToLowerInvariant(),
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        };
        db.Channels.Add(channel);
        return channel;
    }

    private static Pipeline SeedPipeline(AuthDbContext db, Guid broadcasterId, string name)
    {
        Pipeline pipeline = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = broadcasterId,
            Name = name,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        };
        db.Pipelines.Add(pipeline);
        return pipeline;
    }

    private static ScheduledPipelineTask SeedTask(
        AuthDbContext db,
        Guid broadcasterId,
        Guid pipelineId,
        string pipelineName,
        string status,
        DateTimeOffset dueAt
    )
    {
        ScheduledPipelineTask task = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = broadcasterId,
            PipelineId = pipelineId,
            PipelineName = pipelineName,
            DueAt = dueAt,
            Status = status,
            TriggeredByUserId = "12345",
            TriggeredByDisplayName = "some_viewer",
            CreatedAt = Now,
            FiredAt = status
                is ScheduledPipelineTaskStatus.Fired
                    or ScheduledPipelineTaskStatus.Expired
                ? Now
                : null,
        };
        db.ScheduledPipelineTasks.Add(task);
        return task;
    }

    [Fact]
    public async Task GetScheduledJobQueue_distinguishes_a_failed_job_from_a_queued_one()
    {
        (AdminService sut, AuthDbContext db, _) = Build();
        Channel tenant = SeedChannel(db, "streamer_jobs");
        Pipeline pipeline = SeedPipeline(db, tenant.Id, "auto-revert");
        SeedTask(
            db,
            tenant.Id,
            pipeline.Id,
            pipeline.Name,
            ScheduledPipelineTaskStatus.Pending,
            Now.AddMinutes(5)
        );
        SeedTask(
            db,
            tenant.Id,
            pipeline.Id,
            pipeline.Name,
            ScheduledPipelineTaskStatus.Expired,
            Now.AddMinutes(-20)
        );
        await db.SaveChangesAsync();

        Result<PagedList<AdminScheduledJobDto>> result = await sut.GetScheduledJobQueueAsync(
            new PaginationParams(1, 25)
        );

        result.IsSuccess.Should().BeTrue();
        AdminScheduledJobDto queued = result.Value.Items.Single(j => j.Status == "pending");
        AdminScheduledJobDto failed = result.Value.Items.Single(j => j.Status == "expired");
        queued.DisplayState.Should().Be("queued");
        queued.CanRetry.Should().BeFalse();
        failed.DisplayState.Should().Be("failed");
        failed.CanRetry.Should().BeTrue();
    }

    [Fact]
    public async Task GetScheduledJobQueue_changes_when_the_underlying_job_state_changes()
    {
        (AdminService sut, AuthDbContext db, _) = Build();
        Channel tenant = SeedChannel(db, "streamer_flaky_job");
        Pipeline pipeline = SeedPipeline(db, tenant.Id, "feather-hide");
        ScheduledPipelineTask task = SeedTask(
            db,
            tenant.Id,
            pipeline.Id,
            pipeline.Name,
            ScheduledPipelineTaskStatus.Pending,
            Now.AddMinutes(1)
        );
        await db.SaveChangesAsync();

        Result<PagedList<AdminScheduledJobDto>> before = await sut.GetScheduledJobQueueAsync(
            new PaginationParams(1, 25)
        );
        before.Value.Items.Single().DisplayState.Should().Be("queued");

        task.Status = ScheduledPipelineTaskStatus.Expired;
        task.FiredAt = Now;
        await db.SaveChangesAsync();

        Result<PagedList<AdminScheduledJobDto>> after = await sut.GetScheduledJobQueueAsync(
            new PaginationParams(1, 25)
        );
        after.Value.Items.Single().DisplayState.Should().Be("failed");
    }

    [Fact]
    public async Task RetryScheduledJob_reruns_the_job_and_appends_a_new_attempt_leaving_the_original_readable()
    {
        (AdminService sut, AuthDbContext db, _) = Build();
        Channel tenant = SeedChannel(db, "streamer_retry");
        Pipeline pipeline = SeedPipeline(db, tenant.Id, "voice-swap-revert");
        ScheduledPipelineTask original = SeedTask(
            db,
            tenant.Id,
            pipeline.Id,
            pipeline.Name,
            ScheduledPipelineTaskStatus.Expired,
            Now.AddMinutes(-30)
        );
        await db.SaveChangesAsync();

        Guid actorUserId = Guid.NewGuid();
        Result<AdminScheduledJobRetryResultDto> result = await sut.RetryScheduledJobAsync(
            original.Id,
            actorUserId
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.OriginalTaskId.Should().Be(original.Id);
        result.Value.NewTaskId.Should().NotBe(original.Id);

        // The original attempt is untouched — still readable, still expired.
        ScheduledPipelineTask reread = db.ScheduledPipelineTasks.Single(t => t.Id == original.Id);
        reread.Status.Should().Be(ScheduledPipelineTaskStatus.Expired);

        // A brand-new pending row was appended for the same pipeline.
        ScheduledPipelineTask retried = db.ScheduledPipelineTasks.Single(t =>
            t.Id == result.Value.NewTaskId
        );
        retried.Status.Should().Be(ScheduledPipelineTaskStatus.Pending);
        retried.PipelineId.Should().Be(pipeline.Id);
        db.ScheduledPipelineTasks.Count().Should().Be(2);

        IamAuditLog audit = db.IamAuditLogs.Single();
        audit.PrincipalId.Should().Be(actorUserId);
        audit.Permission.Should().Be("scheduled_job:retry");
        audit.TargetBroadcasterId.Should().Be(tenant.Id);
        audit.Justification.Should().Contain(original.Id.ToString());
        audit.Justification.Should().Contain(retried.Id.ToString());
    }

    [Fact]
    public async Task RetryScheduledJob_is_rejected_when_the_job_already_succeeded()
    {
        (AdminService sut, AuthDbContext db, _) = Build();
        Channel tenant = SeedChannel(db, "streamer_already_ran");
        Pipeline pipeline = SeedPipeline(db, tenant.Id, "one-shot");
        ScheduledPipelineTask fired = SeedTask(
            db,
            tenant.Id,
            pipeline.Id,
            pipeline.Name,
            ScheduledPipelineTaskStatus.Fired,
            Now.AddMinutes(-5)
        );
        await db.SaveChangesAsync();

        Result<AdminScheduledJobRetryResultDto> result = await sut.RetryScheduledJobAsync(
            fired.Id,
            Guid.NewGuid()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_RETRYABLE");
        db.ScheduledPipelineTasks.Count().Should().Be(1); // nothing was appended
        db.IamAuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task RetryScheduledJob_is_rejected_when_the_target_pipeline_no_longer_exists()
    {
        (AdminService sut, AuthDbContext db, _) = Build();
        Channel tenant = SeedChannel(db, "streamer_orphan_job");
        Guid deletedPipelineId = Guid.NewGuid();
        ScheduledPipelineTask orphan = SeedTask(
            db,
            tenant.Id,
            deletedPipelineId,
            "long-gone-pipeline",
            ScheduledPipelineTaskStatus.Expired,
            Now.AddMinutes(-30)
        );
        await db.SaveChangesAsync();

        Result<AdminScheduledJobRetryResultDto> result = await sut.RetryScheduledJobAsync(
            orphan.Id,
            Guid.NewGuid()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("TARGET_GONE");
        db.ScheduledPipelineTasks.Count().Should().Be(1);
        db.IamAuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTenantUsage_is_computed_from_real_usage_records_and_never_crosses_tenants()
    {
        (AdminService sut, AuthDbContext db, _) = Build();
        Channel tenantA = SeedChannel(db, "streamer_usage_a");
        Channel tenantB = SeedChannel(db, "streamer_usage_b");
        DateTime periodStart = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime periodEnd = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        db.UsageRecords.Add(
            new UsageRecord
            {
                BroadcasterId = tenantA.Id,
                MetricKey = "chat_messages",
                Quantity = 500,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CreatedAt = Now.UtcDateTime,
            }
        );
        db.UsageRecords.Add(
            new UsageRecord
            {
                BroadcasterId = tenantB.Id,
                MetricKey = "chat_messages",
                Quantity = 9_000,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CreatedAt = Now.UtcDateTime,
            }
        );
        db.TtsUsageRecords.Add(
            new TtsUsageRecord
            {
                BroadcasterId = tenantA.Id,
                UserId = "1",
                CharacterCount = 42,
                Provider = "azure",
                VoiceId = "en-US-Standard",
                OccurredAt = periodStart.AddDays(3),
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();

        Result<PagedList<AdminTenantUsageDto>> result = await sut.GetTenantUsageAsync(
            new PaginationParams(1, 25)
        );

        result.IsSuccess.Should().BeTrue();
        AdminTenantUsageDto usageA = result.Value.Items.Single(u => u.BroadcasterId == tenantA.Id);
        AdminTenantUsageDto usageB = result.Value.Items.Single(u => u.BroadcasterId == tenantB.Id);

        usageA.PeriodStart.Should().Be(periodStart);
        usageA.PeriodEnd.Should().Be(periodEnd);
        usageA.Metrics.Single(m => m.MetricKey == "chat_messages").Quantity.Should().Be(500);
        usageA.TtsCharacterCount.Should().Be(42);

        usageB.Metrics.Single(m => m.MetricKey == "chat_messages").Quantity.Should().Be(9_000);
        usageB.TtsCharacterCount.Should().Be(0); // tenant A's TTS usage never counts toward tenant B
    }
}
