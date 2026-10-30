// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Admin.Diagnostics;
using NomNomzBot.Application.Admin.Dtos;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Infrastructure.Admin.Diagnostics;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// S-ADMIN-DIAG-READ: a platform admin reads one channel's data through the diagnostics family without acting
/// as anyone and without giving a reason (Stoney, 2026-10-30). Runs the REAL controller, diagnostics service,
/// sections, pipeline-execution query service and SaaS <see cref="PlatformIamService"/> against a SQLite
/// database, and proves the returned fields, the internal IAM audit row, the 403 for a non-admin and the
/// plain refusal of an unknown section.
/// </summary>
public sealed class AdminChannelDiagnosticsTests
{
    private static readonly Guid OperatorUser = Guid.Parse("0199a000-0000-7000-8000-000000000d01");
    private static readonly Guid OwnerUser = Guid.Parse("0199a000-0000-7000-8000-000000000d02");
    private static readonly Guid Viewer = Guid.Parse("0199a000-0000-7000-8000-000000000d03");
    private static readonly Guid Broadcaster = Guid.Parse("0199a000-0000-7000-8000-000000000d10");
    private static readonly Guid CommandPipeline = Guid.Parse(
        "0199a000-0000-7000-8000-000000000d20"
    );
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private const string StepLogsJson = """
        [{"StepIndex":0,"ActionType":"chat.send","Succeeded":true,"DurationMs":40,"ErrorMessage":null},
         {"StepIndex":1,"ActionType":"tts.speak","Succeeded":false,"DurationMs":9000,"ErrorMessage":"Execution exceeded its time budget."}]
        """;

    private static AdminChannelDiagnosticsController BuildController(
        ApiTestDbContext db,
        Guid callerUserId
    )
    {
        PlatformIamService iam = new(
            db,
            Substitute.For<IEventBus>(),
            TimeProvider.System,
            new(DeploymentMode.Saas)
        );
        PipelineExecutionQueryService runs = new(db);
        List<IChannelDiagnosticSection> sections =
        [
            new ChannelSummaryDiagnosticSection(db, runs, new FakeTimeProvider(Now)),
            new ChannelRunsDiagnosticSection(runs),
            new ChannelTtsDiagnosticSection(db, new FakeTimeProvider(Now)),
            // Its rows are proven in NomNomzBot.Infrastructure.Tests; here it only has to be a known key.
            new ChannelConnectionsDiagnosticSection([]),
        ];
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(callerUserId.ToString());

        return new(
            new ChannelDiagnosticsService(db, iam, runs, sections),
            currentUser,
            new IamCallerPrincipalResolverService(iam)
        );
    }

    private static async Task<(ApiTestDbContext Db, IamPrincipal Operator)> SeedAsync()
    {
        ApiTestDbContext db = ApiTestDbContext.New();

        IamPrincipal principal = new()
        {
            PrincipalType = IamPrincipalType.Employee,
            UserId = OperatorUser,
            Name = "platform-operator",
            IsActive = true,
        };
        IamRole role = new() { Name = "tenant-reader", IsSystem = true };
        IamPermission permission = new()
        {
            Key = IamPermissionKeys.TenantRead,
            Category = IamCategory.Iam,
        };
        db.IamPrincipals.Add(principal);
        db.IamRoles.Add(role);
        db.IamPermissions.Add(permission);
        db.IamRolePermissions.Add(new() { RoleId = role.Id, PermissionId = permission.Id });
        db.IamRoleAssignments.Add(
            new()
            {
                PrincipalId = principal.Id,
                RoleId = role.Id,
                AssignedByPrincipalId = principal.Id,
            }
        );

        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                Name = "made_up_streamer",
                NameNormalized = "made_up_streamer",
                OwnerUserId = OwnerUser,
                BillingTierKey = "pro",
                IsLive = true,
            }
        );
        db.BillingTiers.Add(
            new()
            {
                Key = "pro",
                DisplayName = "Pro",
                PriceCents = 999,
                Currency = "usd",
                IsPublic = true,
                SortOrder = 1,
            }
        );

        db.PipelineExecutions.AddRange(
            new PipelineExecution
            {
                PipelineId = CommandPipeline,
                BroadcasterId = Broadcaster,
                TriggeredByUserId = Viewer,
                TriggerKind = "command",
                Status = "completed",
                HostCallCount = 2,
                DurationMs = 120,
                StartedAt = Now.AddHours(-2),
                CompletedAt = Now.AddHours(-2),
            },
            new PipelineExecution
            {
                PipelineId = CommandPipeline,
                BroadcasterId = Broadcaster,
                TriggeredByUserId = Viewer,
                TriggerKind = "command",
                Status = "timed_out",
                HostCallCount = 3,
                DurationMs = 9040,
                ErrorMessage = "Execution exceeded its time budget.",
                StepLogsJson = StepLogsJson,
                StartedAt = Now.AddHours(-1),
                CompletedAt = Now.AddHours(-1),
            },
            new PipelineExecution
            {
                PipelineId = CommandPipeline,
                BroadcasterId = Broadcaster,
                TriggerKind = "event",
                Status = "failed",
                HostCallCount = 1,
                DurationMs = 15,
                ErrorMessage = "An older failure outside the day window.",
                StartedAt = Now.AddHours(-30),
                CompletedAt = Now.AddHours(-30),
            }
        );
        await db.SaveChangesAsync();
        return (db, principal);
    }

    [Fact]
    public async Task An_admin_reads_another_channels_summary_and_failed_runs_with_no_reason_asked()
    {
        (ApiTestDbContext db, IamPrincipal principal) = await SeedAsync();
        AdminChannelDiagnosticsController controller = BuildController(db, OperatorUser);

        IActionResult result = await controller.GetDiagnostics(
            Broadcaster,
            "summary,runs",
            new PageRequestDto { Page = 1, Take = 25 },
            failuresOnly: true,
            pipelineId: null,
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();
        ChannelDiagnosticsDto body = (
            (StatusResponseDto<ChannelDiagnosticsDto>)((OkObjectResult)result).Value!
        ).Data!;
        body.BroadcasterId.Should().Be(Broadcaster);

        ChannelDiagnosticSummaryDto summary = body.Summary!;
        summary.ChannelName.Should().Be("made_up_streamer");
        summary.TierKey.Should().Be("pro");
        summary.TierDisplayName.Should().Be("Pro");
        summary.IsLive.Should().BeTrue();
        summary.RunsLast24Hours.Should().Be(2);
        summary.FailedRunsLast24Hours.Should().Be(1);
        summary.LastFailedRunAt.Should().Be(Now.AddHours(-1));
        summary.LastFailedRunError.Should().Be("Execution exceeded its time budget.");
        summary.LastFailedRunId.Should().NotBeNull();

        ChannelDiagnosticRunsDto runs = body.Runs!;
        runs.TotalCount.Should().Be(2);
        runs.Page.Should().Be(1);
        runs.HasMore.Should().BeFalse();
        runs.Items.Select(r => r.Status).Should().Equal("timed_out", "failed");
        PipelineExecutionSummaryDto newest = runs.Items[0];
        newest.Id.Should().Be(summary.LastFailedRunId!.Value);
        newest.PipelineId.Should().Be(CommandPipeline);
        newest.TriggerKind.Should().Be("command");
        newest.TriggeredByUserId.Should().Be(Viewer);
        newest.DurationMs.Should().Be(9040);
        newest.HostCallCount.Should().Be(3);
        newest.ErrorMessage.Should().Be("Execution exceeded its time budget.");
        newest.StartedAt.Should().Be(Now.AddHours(-1));

        IamAuditLog audit = await db.IamAuditLogs.AsNoTracking().SingleAsync();
        audit.PrincipalId.Should().Be(principal.Id);
        audit.Permission.Should().Be(IamPermissionKeys.TenantRead);
        audit.TargetBroadcasterId.Should().Be(Broadcaster);
        audit.TargetResource.Should().Be("diagnostics:summary,runs");
        audit.Justification.Should().BeNull();
        audit.Outcome.Should().Be(IamOutcome.Allowed);
    }

    private static TtsUsageRecord TtsRow(
        Guid channel,
        int minutesAgo,
        int? synthesisMs,
        bool failed = false,
        string provider = "edge"
    ) =>
        new()
        {
            BroadcasterId = channel,
            UserId = "viewer-1",
            CharacterCount = 40 + minutesAgo,
            Provider = provider,
            VoiceId = "en-US-AnaNeural",
            Text = "made up line",
            SynthesisMs = synthesisMs,
            Failed = failed,
            OccurredAt = Now.AddMinutes(-minutesAgo),
        };

    [Fact]
    public async Task An_admin_reads_the_tts_clips_and_the_24_hour_synthesis_figures()
    {
        (ApiTestDbContext db, IamPrincipal principal) = await SeedAsync();
        Guid otherChannel = Guid.Parse("0199a000-0000-7000-8000-000000000d11");
        db.TtsUsageRecords.AddRange(
            TtsRow(Broadcaster, 10, 1000),
            TtsRow(Broadcaster, 20, 2000),
            TtsRow(Broadcaster, 30, 3000, provider: "azure"),
            TtsRow(Broadcaster, 40, 4000),
            TtsRow(Broadcaster, 50, 20000, failed: true),
            TtsRow(Broadcaster, 180, null),
            TtsRow(Broadcaster, 30 * 60, 9999),
            TtsRow(otherChannel, 5, 77777, failed: true)
        );
        await db.SaveChangesAsync();
        AdminChannelDiagnosticsController controller = BuildController(db, OperatorUser);

        IActionResult result = await controller.GetDiagnostics(
            Broadcaster,
            "tts",
            new PageRequestDto(),
            failuresOnly: false,
            pipelineId: null,
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();
        ChannelDiagnosticsDto body = (
            (StatusResponseDto<ChannelDiagnosticsDto>)((OkObjectResult)result).Value!
        ).Data!;
        body.Summary.Should().BeNull();
        body.Runs.Should().BeNull();
        ChannelDiagnosticTtsDto tts = body.Tts!;

        tts.Clips.Select(c => c.SynthesisMs)
            .Should()
            .Equal(1000, 2000, 3000, 4000, 20000, null, 9999);
        ChannelDiagnosticTtsClipDto newest = tts.Clips[0];
        newest.OccurredAt.Should().Be(Now.AddMinutes(-10));
        newest.Provider.Should().Be("edge");
        newest.VoiceId.Should().Be("en-US-AnaNeural");
        newest.CharacterCount.Should().Be(50);
        newest.Failed.Should().BeFalse();
        tts.Clips[2].Provider.Should().Be("azure");
        tts.Clips[4].Failed.Should().BeTrue();
        tts.Clips[5].SynthesisMs.Should().BeNull("rows written before the timing stay empty");

        tts.Last24Hours.Clips.Should().Be(6);
        tts.Last24Hours.FailedClips.Should().Be(1);
        tts.Last24Hours.AverageSynthesisMs.Should().Be(6000);
        tts.Last24Hours.P95SynthesisMs.Should().Be(20000);

        IamAuditLog audit = await db.IamAuditLogs.AsNoTracking().SingleAsync();
        audit.PrincipalId.Should().Be(principal.Id);
        audit.TargetResource.Should().Be("diagnostics:tts");
        audit.Outcome.Should().Be(IamOutcome.Allowed);
    }

    [Fact]
    public async Task The_tts_section_lists_the_last_50_clips_and_counts_the_whole_day()
    {
        (ApiTestDbContext db, _) = await SeedAsync();
        for (int i = 0; i < 55; i++)
            db.TtsUsageRecords.Add(TtsRow(Broadcaster, 60 * 20 - i * 10, 100 * (i + 1)));
        await db.SaveChangesAsync();
        AdminChannelDiagnosticsController controller = BuildController(db, OperatorUser);

        IActionResult result = await controller.GetDiagnostics(
            Broadcaster,
            "tts",
            new PageRequestDto(),
            failuresOnly: false,
            pipelineId: null,
            CancellationToken.None
        );

        ChannelDiagnosticTtsDto tts = (
            (StatusResponseDto<ChannelDiagnosticsDto>)((OkObjectResult)result).Value!
        )
            .Data!
            .Tts!;
        tts.Clips.Should().HaveCount(50);
        tts.Clips[0].SynthesisMs.Should().Be(5500, "the newest clip comes first");
        tts.Clips[49].SynthesisMs.Should().Be(600);
        tts.Last24Hours.Clips.Should().Be(55);
        tts.Last24Hours.FailedClips.Should().Be(0);
        tts.Last24Hours.AverageSynthesisMs.Should().Be(2800);
        tts.Last24Hours.P95SynthesisMs.Should().Be(5300);
    }

    [Fact]
    public async Task A_channel_with_no_tts_clips_gets_an_empty_tts_section()
    {
        (ApiTestDbContext db, _) = await SeedAsync();
        AdminChannelDiagnosticsController controller = BuildController(db, OperatorUser);

        IActionResult result = await controller.GetDiagnostics(
            Broadcaster,
            "tts",
            new PageRequestDto(),
            failuresOnly: false,
            pipelineId: null,
            CancellationToken.None
        );

        ChannelDiagnosticTtsDto tts = (
            (StatusResponseDto<ChannelDiagnosticsDto>)((OkObjectResult)result).Value!
        )
            .Data!
            .Tts!;
        tts.Clips.Should().BeEmpty();
        tts.Last24Hours.Clips.Should().Be(0);
        tts.Last24Hours.FailedClips.Should().Be(0);
        tts.Last24Hours.AverageSynthesisMs.Should().BeNull();
        tts.Last24Hours.P95SynthesisMs.Should().BeNull();
    }

    [Fact]
    public async Task An_admin_reads_one_runs_step_logs_and_the_read_is_audited()
    {
        (ApiTestDbContext db, IamPrincipal principal) = await SeedAsync();
        long runId = await db
            .PipelineExecutions.Where(e => e.Status == "timed_out")
            .Select(e => e.Id)
            .SingleAsync();
        AdminChannelDiagnosticsController controller = BuildController(db, OperatorUser);

        IActionResult result = await controller.GetRun(Broadcaster, runId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        PipelineExecutionDetailDto run = (
            (StatusResponseDto<PipelineExecutionDetailDto>)((OkObjectResult)result).Value!
        ).Data!;
        run.Id.Should().Be(runId);
        run.Status.Should().Be("timed_out");
        run.TriggeredByUserId.Should().Be(Viewer);
        run.StepLogs.Should().HaveCount(2);
        run.StepLogs[0].ActionType.Should().Be("chat.send");
        run.StepLogs[0].Succeeded.Should().BeTrue();
        run.StepLogs[1].StepIndex.Should().Be(1);
        run.StepLogs[1].ActionType.Should().Be("tts.speak");
        run.StepLogs[1].Succeeded.Should().BeFalse();
        run.StepLogs[1].DurationMs.Should().Be(9000);
        run.StepLogs[1].ErrorMessage.Should().Be("Execution exceeded its time budget.");

        IamAuditLog audit = await db.IamAuditLogs.AsNoTracking().SingleAsync();
        audit.PrincipalId.Should().Be(principal.Id);
        audit.Permission.Should().Be(IamPermissionKeys.TenantRead);
        audit.TargetBroadcasterId.Should().Be(Broadcaster);
        audit.TargetResource.Should().Be($"diagnostics:runs:{runId}");
        audit.Outcome.Should().Be(IamOutcome.Allowed);
    }

    [Fact]
    public async Task A_channel_owner_who_is_not_a_platform_admin_gets_403_and_no_data()
    {
        (ApiTestDbContext db, _) = await SeedAsync();
        AdminChannelDiagnosticsController controller = BuildController(db, OwnerUser);

        IActionResult list = await controller.GetDiagnostics(
            Broadcaster,
            "summary,runs,tts,connections",
            new PageRequestDto(),
            failuresOnly: false,
            pipelineId: null,
            CancellationToken.None
        );
        long anyRun = await db.PipelineExecutions.Select(e => e.Id).FirstAsync();
        IActionResult detail = await controller.GetRun(Broadcaster, anyRun, CancellationToken.None);

        list.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        ((ObjectResult)list).Value.Should().NotBeOfType<StatusResponseDto<ChannelDiagnosticsDto>>();
        detail.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(nameof(AdminChannelDiagnosticsController.GetDiagnostics))]
    [InlineData(nameof(AdminChannelDiagnosticsController.GetRun))]
    public void Both_reads_are_gated_on_the_tenant_read_platform_permission(string methodName)
    {
        MethodInfo method = typeof(AdminChannelDiagnosticsController).GetMethod(methodName)!;

        method
            .GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Policy)
            .Should()
            .Contain(IamPermissionKeys.TenantRead);
    }

    [Fact]
    public async Task A_section_that_does_not_exist_is_refused_with_a_plain_reason()
    {
        (ApiTestDbContext db, _) = await SeedAsync();
        AdminChannelDiagnosticsController controller = BuildController(db, OperatorUser);

        IActionResult result = await controller.GetDiagnostics(
            Broadcaster,
            "summary,bogus",
            new PageRequestDto(),
            failuresOnly: false,
            pipelineId: null,
            CancellationToken.None
        );

        result.Should().BeOfType<BadRequestObjectResult>();
        StatusResponseDto<object> body =
            (StatusResponseDto<object>)((BadRequestObjectResult)result).Value!;
        body.Message.Should()
            .Be(
                "Unknown diagnostic section 'bogus'. Known sections: connections, runs, summary, tts."
            );
        (await db.IamAuditLogs.AsNoTracking().CountAsync()).Should().Be(0);
    }
}
