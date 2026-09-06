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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The platform-wide trust &amp; safety desk (S-ADMIN-8a) against a real database: cross-tenant abuse
/// correlation computed from real recorded <see cref="SpamDetection"/> rows only, a review queue over the
/// automatic account actions the spam-defence engine actually took, and an overturn that REVERSES the real
/// Twitch timeout rather than only flipping the row.
/// </summary>
public sealed class TrustSafetyReviewServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("0199d000-0000-7000-8000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("0199d000-0000-7000-8000-0000000000b1");
    private static readonly Guid TenantC = Guid.Parse("0199d000-0000-7000-8000-0000000000c1");
    private static readonly Guid OperatorId = Guid.Parse("0199d000-0000-7000-8000-000000000c01");
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private const string Why = "Ticket #4471 — cross-channel raid-chat spam.";

    private readonly SqliteConnection _connection;
    private readonly FakeTimeProvider _time = new(Now);

    public TrustSafetyReviewServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = NewDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        foreach (
            (Guid id, string name) in new[]
            {
                (TenantA, "channel-a"),
                (TenantB, "channel-b"),
                (TenantC, "channel-c"),
            }
        )
        {
            db.Channels.Add(
                new Channel
                {
                    Id = id,
                    OwnerUserId = Guid.NewGuid(),
                    Provider = AuthEnums.Platform.Twitch,
                    ExternalChannelId = $"ext-{name}",
                    Name = name,
                    NameNormalized = name,
                }
            );
        }
        db.SaveChanges();
    }

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private TrustSafetyReviewService NewService(
        AppDbContext db,
        IModerationService? moderation = null,
        DeploymentMode mode = DeploymentMode.Saas
    ) =>
        new(
            db,
            new PlatformIamService(db, new RecordingEventBus(), TimeProvider.System, new(mode)),
            moderation ?? Substitute.For<IModerationService>(),
            _time
        );

    /// <summary>Seeds an operator IAM principal holding exactly <paramref name="permissionKeys"/>.</summary>
    private static void SeedOperator(
        AppDbContext db,
        Guid principalId,
        params string[] permissionKeys
    )
    {
        Guid roleId = Guid.NewGuid();
        db.IamPrincipals.Add(
            new()
            {
                Id = principalId,
                PrincipalType = IamPrincipalType.Employee,
                Name = "trust-safety-operator",
                IsActive = true,
            }
        );
        db.IamRoles.Add(new() { Id = roleId, Name = $"role-{roleId}" });
        foreach (string key in permissionKeys)
        {
            Guid permissionId = Guid.NewGuid();
            db.IamPermissions.Add(
                new()
                {
                    Id = permissionId,
                    Key = key,
                    Category = IamCategory.Tenant,
                }
            );
            db.IamRolePermissions.Add(new() { RoleId = roleId, PermissionId = permissionId });
        }
        db.IamRoleAssignments.Add(
            new()
            {
                PrincipalId = principalId,
                RoleId = roleId,
                AssignedByPrincipalId = principalId,
            }
        );
        db.SaveChanges();
    }

    private static SpamDetection Detection(
        Guid broadcasterId,
        string subjectPlatformUserId,
        string displayName,
        DateTime detectedAt,
        SpamOutcome outcome = SpamOutcome.Flag,
        bool wasDryRun = true,
        SpamConfidence confidence = SpamConfidence.High,
        string reason = "High confidence — routed to the escalation ladder."
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcasterId,
            SubjectPlatformUserId = subjectPlatformUserId,
            SubjectDisplayName = displayName,
            Provider = AuthEnums.Platform.Twitch,
            MessageId = Guid.NewGuid().ToString(),
            MessageText = "free f0ll0ws check bio",
            Skeleton = "free follows check bio",
            Signals = nameof(SpamConfidence.High),
            Confidence = confidence,
            Tier = SpamTrustTier.Untrusted,
            Outcome = outcome,
            WouldHaveBeen = outcome,
            WasDryRun = wasDryRun,
            Reason = reason,
            DetectedAt = detectedAt,
        };

    public void Dispose() => _connection.Dispose();

    // ---- Cross-tenant correlation is computed from real rows, never a heuristic ------------------

    [Fact]
    public async Task CrossTenantSignals_NamesTheRightActorTenantsAndDetections_AndExcludesUnrelated()
    {
        using AppDbContext seed = NewDbContext();
        SeedOperator(seed, OperatorId, IamPermissionKeys.TrustSafetyReview);

        SpamDetection hitInA = Detection(
            TenantA,
            "raider-1",
            "Raider1",
            Now.UtcDateTime.AddMinutes(-10)
        );
        SpamDetection hitInB = Detection(
            TenantB,
            "raider-1",
            "Raider1",
            Now.UtcDateTime.AddMinutes(-5)
        );
        SpamDetection unrelated = Detection(TenantA, "regular-1", "Regular1", Now.UtcDateTime);
        seed.SpamDetections.AddRange(hitInA, hitInB, unrelated);
        seed.SaveChanges();

        using AppDbContext db = NewDbContext();
        Result<IReadOnlyList<CrossTenantAbuseSignalDto>> result = await NewService(db)
            .GetCrossTenantSignalsAsync(OperatorId, Why);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        CrossTenantAbuseSignalDto signal = result.Value[0];
        signal.SubjectPlatformUserId.Should().Be("raider-1");
        signal.SubjectDisplayName.Should().Be("Raider1");
        signal.TenantCount.Should().Be(2);
        signal.DetectionCount.Should().Be(2);
        signal.Hits.Select(h => h.BroadcasterId).Should().BeEquivalentTo([TenantA, TenantB]);
        signal.Hits.Select(h => h.DetectionId).Should().BeEquivalentTo([hitInA.Id, hitInB.Id]);
        signal.Hits.Should().Contain(h => h.ChannelName == "channel-a");
        signal.Hits.Should().Contain(h => h.ChannelName == "channel-b");
        signal
            .Hits.Select(h => h.DetectionId)
            .Should()
            .NotContain(unrelated.Id, "a single-tenant actor is not a cross-tenant signal");
    }

    [Fact]
    public async Task CrossTenantSignals_OneTenantOnly_IsNotSurfaced()
    {
        using AppDbContext seed = NewDbContext();
        SeedOperator(seed, OperatorId, IamPermissionKeys.TrustSafetyReview);
        seed.SpamDetections.Add(Detection(TenantA, "solo-1", "Solo1", Now.UtcDateTime));
        seed.SaveChanges();

        using AppDbContext db = NewDbContext();
        Result<IReadOnlyList<CrossTenantAbuseSignalDto>> result = await NewService(db)
            .GetCrossTenantSignalsAsync(OperatorId, Why);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    // ---- The review queue carries evidence, and its state changes on review -----------------------

    [Fact]
    public async Task ReviewQueue_ShowsTheAutomaticActionWithItsEvidence_AndConfirmChangesItsState()
    {
        using AppDbContext seed = NewDbContext();
        SeedOperator(seed, OperatorId, IamPermissionKeys.TrustSafetyReview);
        SpamDetection auto = Detection(
            TenantC,
            "bot-1",
            "Bot1",
            Now.UtcDateTime,
            outcome: SpamOutcome.DeleteAndEscalate,
            wasDryRun: false
        );
        SpamDetection dryRun = Detection(
            TenantC,
            "bot-2",
            "Bot2",
            Now.UtcDateTime,
            outcome: SpamOutcome.DeleteAndEscalate,
            wasDryRun: true
        );
        seed.SpamDetections.AddRange(auto, dryRun);
        seed.SaveChanges();

        using AppDbContext db = NewDbContext();
        TrustSafetyReviewService service = NewService(db);
        Result<PagedList<TrustSafetyReviewItemDto>> queue = await service.GetReviewQueueAsync(
            OperatorId,
            Why,
            new PaginationParams(1, 25)
        );

        queue.IsSuccess.Should().BeTrue();
        queue.Value.Items.Should().ContainSingle("dry-run rows never took a real account action");
        TrustSafetyReviewItemDto item = queue.Value.Items[0];
        item.DetectionId.Should().Be(auto.Id);
        item.MessageText.Should().Be(auto.MessageText);
        item.Signals.Should().Be(auto.Signals);
        item.Reason.Should().Be(auto.Reason);
        item.ChannelName.Should().Be("channel-c");
        item.ConfirmedAt.Should().BeNull();

        Result confirm = await service.ConfirmAsync(OperatorId, auto.Id, Why);
        confirm.IsSuccess.Should().BeTrue();

        using AppDbContext readBack = NewDbContext();
        SpamDetection stored = await readBack.SpamDetections.SingleAsync(d => d.Id == auto.Id);
        stored.ConfirmedAt.Should().NotBeNull();
        stored.ConfirmedByUserId.Should().Be(OperatorId);
        stored.OverturnedAt.Should().BeNull("confirming does not touch the action");
    }

    // ---- Overturn reverses the REAL effect, not merely the row ------------------------------------

    [Fact]
    public async Task Overturn_CallsUnban_AndOnlyThenMarksReversedAndAudited()
    {
        using AppDbContext seed = NewDbContext();
        SeedOperator(seed, OperatorId, IamPermissionKeys.TrustSafetyReview);
        SpamDetection auto = Detection(
            TenantA,
            "bot-3",
            "Bot3",
            Now.UtcDateTime,
            outcome: SpamOutcome.DeleteAndEscalate,
            wasDryRun: false
        );
        seed.SpamDetections.Add(auto);
        seed.SaveChanges();

        IModerationService moderation = Substitute.For<IModerationService>();
        moderation
            .UnbanAsync(
                TenantA.ToString(),
                Arg.Any<Guid>(),
                "bot-3",
                OperatorId.ToString(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new ModerationActionResult(true, "unbanned")));

        using AppDbContext db = NewDbContext();
        Result overturn = await NewService(db, moderation).OverturnAsync(OperatorId, auto.Id, Why);

        overturn.IsSuccess.Should().BeTrue();
        await moderation
            .Received(1)
            .UnbanAsync(
                TenantA.ToString(),
                Arg.Any<Guid>(),
                "bot-3",
                OperatorId.ToString(),
                Arg.Any<CancellationToken>()
            );

        using AppDbContext readBack = NewDbContext();
        SpamDetection stored = await readBack.SpamDetections.SingleAsync(d => d.Id == auto.Id);
        stored.OverturnedAt.Should().NotBeNull();
        stored
            .OverturnedByUserId.Should()
            .Be(OperatorId, "the reversal must name an accountable operator");

        IamAuditLog audit = await readBack.IamAuditLogs.SingleAsync(a =>
            a.Permission == IamPermissionKeys.TrustSafetyReview
            && a.PrincipalId == OperatorId
            && a.TargetResource == $"overturn:{auto.Id}"
        );
        audit.Outcome.Should().Be(IamOutcome.Allowed);
        audit.Justification.Should().Be(Why);
    }

    [Fact]
    public async Task Overturn_WhenTheRealReversalFails_LeavesTheDetectionUntouched()
    {
        using AppDbContext seed = NewDbContext();
        SeedOperator(seed, OperatorId, IamPermissionKeys.TrustSafetyReview);
        SpamDetection auto = Detection(
            TenantB,
            "bot-4",
            "Bot4",
            Now.UtcDateTime,
            outcome: SpamOutcome.DeleteAndEscalate,
            wasDryRun: false
        );
        seed.SpamDetections.Add(auto);
        seed.SaveChanges();

        IModerationService moderation = Substitute.For<IModerationService>();
        moderation
            .UnbanAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<ModerationActionResult>("Twitch is unreachable.", "transport"));

        using AppDbContext db = NewDbContext();
        Result overturn = await NewService(db, moderation).OverturnAsync(OperatorId, auto.Id, Why);

        overturn.IsFailure.Should().BeTrue();
        overturn.ErrorCode.Should().Be("REVERSAL_FAILED");

        using AppDbContext readBack = NewDbContext();
        SpamDetection stored = await readBack.SpamDetections.SingleAsync(d => d.Id == auto.Id);
        stored
            .OverturnedAt.Should()
            .BeNull("a claim of reversal must never outrun what actually happened");
        stored.OverturnedByUserId.Should().BeNull();
    }

    // ---- Permission gate: refused as an authorization failure, never a quietly-empty list ----------

    [Fact]
    public async Task WithoutThePermission_CrossTenantSignalsIsRefused_NotAnEmptyList()
    {
        using AppDbContext seed = NewDbContext();
        SeedOperator(seed, OperatorId, IamPermissionKeys.UserSupportView);
        seed.SpamDetections.AddRange(
            Detection(TenantA, "raider-9", "Raider9", Now.UtcDateTime),
            Detection(TenantB, "raider-9", "Raider9", Now.UtcDateTime)
        );
        seed.SaveChanges();

        using AppDbContext db = NewDbContext();
        Result<IReadOnlyList<CrossTenantAbuseSignalDto>> result = await NewService(db)
            .GetCrossTenantSignalsAsync(OperatorId, Why);

        result
            .IsFailure.Should()
            .BeTrue("holding a different key must not imply trust-safety:review");
        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task WithoutThePermission_ReviewQueueIsRefused_NotAnEmptyList()
    {
        using AppDbContext seed = NewDbContext();
        SeedOperator(seed, OperatorId, IamPermissionKeys.UserSupportView);
        seed.SpamDetections.Add(
            Detection(
                TenantA,
                "bot-5",
                "Bot5",
                Now.UtcDateTime,
                outcome: SpamOutcome.DeleteAndEscalate,
                wasDryRun: false
            )
        );
        seed.SaveChanges();

        using AppDbContext db = NewDbContext();
        Result<PagedList<TrustSafetyReviewItemDto>> result = await NewService(db)
            .GetReviewQueueAsync(OperatorId, Why, new PaginationParams(1, 25));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
    }
}
