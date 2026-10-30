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

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Platform;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Billing.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Billing;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// S-ADMIN-4b: proves a comp/entitlement grant through <see cref="AdminBillingController"/> —
/// (1) issuing one PERSISTS the grant row plus an <see cref="IamAuditLog"/> entry (not merely a 200), (2) the
/// grant is actually READ BY the entitlement check (<see cref="IBillingTierService.IsTierAtLeastAsync"/>, the
/// same gate <c>RequireTierAction</c> uses), denying before it exists and allowing once it is live, and
/// (3) an EXPIRED grant does not confer the capability — a row nothing evaluates, or evaluates past its
/// expiry, must not act as if it grants anything.
/// </summary>
public sealed class EntitlementGrantControllerTests
{
    private static readonly Guid Actor = Guid.Parse("0199c000-0000-7000-8000-000000000b01");
    private static readonly Guid Broadcaster = Guid.Parse("0199c000-0000-7000-8000-000000000b02");

    private static AdminBillingController BuildController(
        BillingTierChangeTestDbContext db,
        FakeTimeProvider clock,
        out IEntitlementGrantService grantService,
        IEventBus? eventBus = null
    )
    {
        BillingTierService tiers = new(db, clock, TestUnsubscribedTier.Default());
        grantService = new EntitlementGrantService(
            db,
            tiers,
            clock,
            Substitute.For<IFeatureFlagCacheService>()
        );
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(Actor.ToString());

        return new AdminBillingController(
            Substitute.For<IInviteCodeService>(),
            Substitute.For<ISubscriptionService>(),
            Substitute.For<IBillingTierAdminService>(),
            grantService,
            Substitute.For<IPricedUnitAdminService>(),
            currentUser,
            eventBus ?? Substitute.For<IEventBus>()
        );
    }

    private static async Task<(
        BillingTierChangeTestDbContext Db,
        BillingTier BaseTier,
        BillingTier ProTier
    )> SeedChannelOnBaseTierAsync()
    {
        BillingTierChangeTestDbContext db = BillingTierChangeTestDbContext.New();

        BillingTier baseTier = new()
        {
            Key = "base",
            DisplayName = "Base",
            PriceCents = 0,
            Currency = "usd",
            IsPublic = true,
            SortOrder = 0,
        };
        BillingTier proTier = new()
        {
            Key = "pro",
            DisplayName = "Pro",
            PriceCents = 999,
            Currency = "usd",
            IsPublic = true,
            SortOrder = 1,
        };
        db.BillingTiers.AddRange(baseTier, proTier);
        db.TierLimits.AddRange(
            new TierLimit
            {
                TierId = baseTier.Id,
                LimitKey = "custom_commands",
                LimitValue = 10,
            },
            new TierLimit
            {
                TierId = proTier.Id,
                LimitKey = "custom_commands",
                LimitValue = 500,
            }
        );
        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                Name = "tenant",
                NameNormalized = "tenant",
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
            }
        );

        await db.SaveChangesAsync();
        return (db, baseTier, proTier);
    }

    // A per-tenant override keeps winning after the grant, so a key it covers is not a change the grant makes.
    [Fact]
    public async Task The_grant_preview_does_not_count_a_limit_a_tenant_override_holds()
    {
        (BillingTierChangeTestDbContext db, _, BillingTier proTier) =
            await SeedChannelOnBaseTierAsync();
        db.TenantLimitOverrides.Add(
            new TenantLimitOverride
            {
                BroadcasterId = Broadcaster,
                LimitKey = "custom_commands",
                LimitValue = 42,
                Reason = "support case",
            }
        );
        await db.SaveChangesAsync();
        FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-09-05T00:00:00Z"));
        BuildController(db, clock, out IEntitlementGrantService grantService);

        EntitlementGrantPreviewDto preview = (
            await grantService.PreviewGrantAsync(Broadcaster, proTier.Id)
        ).Value;

        preview.ChangedLimitCount.Should().Be(0);
        preview.ChangedLimitKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Issuing_a_grant_persists_it_and_writes_an_audit_entry()
    {
        (BillingTierChangeTestDbContext db, _, BillingTier proTier) =
            await SeedChannelOnBaseTierAsync();
        FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-09-05T00:00:00Z"));
        AdminBillingController controller = BuildController(db, clock, out _);

        IssueEntitlementGrantRequest request = new(
            TierId: proTier.Id,
            Reason: "Support case #4471 — negotiated 30-day comp",
            ExpiresAt: new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
            ConfirmedChangedLimitCount: 1
        );

        IActionResult result = await controller.IssueGrant(
            Broadcaster,
            request,
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();

        // Read back from a FRESH query — proves the write actually landed, not merely that the endpoint
        // answered 200.
        EntitlementGrant persisted = await db
            .EntitlementGrants.AsNoTracking()
            .SingleAsync(g => g.BroadcasterId == Broadcaster);
        persisted.GrantedTierId.Should().Be(proTier.Id);
        persisted.Reason.Should().Be("Support case #4471 — negotiated 30-day comp");
        persisted.ExpiresAt.Should().Be(new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc));
        persisted.IssuedByAdminId.Should().Be(Actor);

        IamAuditLog auditEntry = await db
            .IamAuditLogs.AsNoTracking()
            .SingleAsync(a =>
                a.TargetBroadcasterId == Broadcaster && a.Permission == "entitlement_grant:issue"
            );
        auditEntry.PrincipalId.Should().Be(Actor);
        auditEntry.TargetResource.Should().Be("pro");
        auditEntry.AffectedTenantCount.Should().Be(1);
        auditEntry.Justification.Should().Contain("Support case #4471");
    }

    [Fact]
    public async Task Preview_counts_the_limit_keys_that_would_actually_change()
    {
        (BillingTierChangeTestDbContext db, _, BillingTier proTier) =
            await SeedChannelOnBaseTierAsync();
        FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-09-05T00:00:00Z"));
        BuildController(db, clock, out IEntitlementGrantService grantService);

        Result<EntitlementGrantPreviewDto> preview = await grantService.PreviewGrantAsync(
            Broadcaster,
            proTier.Id,
            CancellationToken.None
        );

        preview.IsSuccess.Should().BeTrue();
        preview.Value.CurrentTierKey.Should().Be("base");
        preview.Value.GrantedTierKey.Should().Be("pro");
        preview.Value.ChangedLimitCount.Should().Be(1);
        preview
            .Value.ChangedLimitKeys.Should()
            .ContainSingle()
            .Which.Should()
            .Be("custom_commands");
    }

    [Fact]
    public async Task Issuing_with_a_stale_confirmed_count_fails_closed_and_does_not_persist()
    {
        (BillingTierChangeTestDbContext db, _, BillingTier proTier) =
            await SeedChannelOnBaseTierAsync();
        FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-09-05T00:00:00Z"));
        AdminBillingController controller = BuildController(db, clock, out _);

        // The caller previewed at 1 changed key, but the target tier gained another limit in the meantime.
        db.TierLimits.Add(
            new TierLimit
            {
                TierId = proTier.Id,
                LimitKey = "concurrent_streams",
                LimitValue = 3,
            }
        );
        await db.SaveChangesAsync();

        IssueEntitlementGrantRequest request = new(
            TierId: proTier.Id,
            Reason: "Stale preview",
            ExpiresAt: new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
            ConfirmedChangedLimitCount: 1 // stale — reality is now 2
        );

        IActionResult result = await controller.IssueGrant(
            Broadcaster,
            request,
            CancellationToken.None
        );

        ObjectResult objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        (await db.EntitlementGrants.AsNoTracking().AnyAsync(g => g.BroadcasterId == Broadcaster))
            .Should()
            .BeFalse("a stale-count issue must not silently apply");
    }

    [Fact]
    public async Task A_live_grant_is_read_by_the_real_entitlement_check_and_elevates_the_gate()
    {
        (BillingTierChangeTestDbContext db, _, BillingTier proTier) =
            await SeedChannelOnBaseTierAsync();
        FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-09-05T00:00:00Z"));
        BillingTierService tiers = new(db, clock, TestUnsubscribedTier.Default());
        AdminBillingController controller = BuildController(
            db,
            clock,
            out IEntitlementGrantService grantService
        );

        // Before the grant: the tenant is on "base" and does not clear the "pro" gate — the exact check
        // RequireTierAction runs on every pipeline execution.
        Result<bool> beforeGrant = await tiers.IsTierAtLeastAsync(
            Broadcaster,
            "pro",
            CancellationToken.None
        );
        beforeGrant.IsSuccess.Should().BeTrue();
        beforeGrant.Value.Should().BeFalse("the tenant has no subscription and no grant yet");

        IssueEntitlementGrantRequest request = new(
            TierId: proTier.Id,
            Reason: "Comp for a support escalation",
            ExpiresAt: new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
            ConfirmedChangedLimitCount: 1
        );
        IActionResult issueResult = await controller.IssueGrant(
            Broadcaster,
            request,
            CancellationToken.None
        );
        issueResult.Should().BeOfType<OkObjectResult>();

        // After the grant: the SAME real code path now clears the gate — a grant nothing evaluates would
        // never move this from false to true.
        Result<bool> afterGrant = await tiers.IsTierAtLeastAsync(
            Broadcaster,
            "pro",
            CancellationToken.None
        );
        afterGrant.IsSuccess.Should().BeTrue();
        afterGrant.Value.Should().BeTrue("the live comp elevates the tenant to the pro tier");

        // The entitlement view itself also reflects the comped tier's limit.
        Result<long> limit = await tiers.GetLimitAsync(
            Broadcaster,
            "custom_commands",
            CancellationToken.None
        );
        limit.IsSuccess.Should().BeTrue();
        limit.Value.Should().Be(500);
    }

    [Fact]
    public async Task An_expired_grant_does_not_confer_the_capability()
    {
        (BillingTierChangeTestDbContext db, _, BillingTier proTier) =
            await SeedChannelOnBaseTierAsync();
        DateTimeOffset now = DateTimeOffset.Parse("2026-09-05T00:00:00Z");
        FakeTimeProvider clock = new(now);
        BillingTierService tiers = new(db, clock, TestUnsubscribedTier.Default());

        // Inserted directly (bypassing IssueGrantAsync's future-expiry validation) to model a grant that has
        // since lapsed — the resolution path, not the issuing path, is what must refuse to honor it.
        db.EntitlementGrants.Add(
            new EntitlementGrant
            {
                BroadcasterId = Broadcaster,
                GrantedTierId = proTier.Id,
                Reason = "Expired comp",
                ExpiresAt = now.UtcDateTime.AddDays(-1),
                IssuedAt = now.UtcDateTime.AddDays(-31),
                IssuedByAdminId = Actor,
            }
        );
        await db.SaveChangesAsync();

        Result<bool> gate = await tiers.IsTierAtLeastAsync(
            Broadcaster,
            "pro",
            CancellationToken.None
        );
        gate.IsSuccess.Should().BeTrue();
        gate.Value.Should()
            .BeFalse("the grant expired a day ago and must not still elevate the tier");

        Result<long> limit = await tiers.GetLimitAsync(
            Broadcaster,
            "custom_commands",
            CancellationToken.None
        );
        limit.IsSuccess.Should().BeTrue();
        limit
            .Value.Should()
            .Be(10, "resolution falls back to the base tier once the grant has lapsed");
    }

    [Fact]
    public async Task Ending_a_forever_gift_stores_now_as_its_end_and_tells_the_operator_desks()
    {
        (BillingTierChangeTestDbContext db, _, BillingTier proTier) =
            await SeedChannelOnBaseTierAsync();
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-29T12:00:00Z");
        FakeTimeProvider clock = new(now);
        IEventBus eventBus = Substitute.For<IEventBus>();
        AdminBillingController controller = BuildController(db, clock, out _, eventBus);

        IActionResult issued = await controller.IssueGrant(
            Broadcaster,
            new IssueEntitlementGrantRequest(
                TierId: proTier.Id,
                Reason: "Community thank-you, forever",
                ConfirmedChangedLimitCount: 1,
                Forever: true
            ),
            CancellationToken.None
        );
        issued.Should().BeOfType<OkObjectResult>();
        EntitlementGrant gift = await db
            .EntitlementGrants.AsNoTracking()
            .SingleAsync(g => g.BroadcasterId == Broadcaster);
        gift.ExpiresAt.Should().BeNull("a forever gift stores no end date");

        clock.Advance(TimeSpan.FromDays(3));
        eventBus.ClearReceivedCalls();

        IActionResult ended = await controller.EndGrant(
            Broadcaster,
            gift.Id,
            CancellationToken.None
        );

        ended.Should().BeOfType<OkObjectResult>();
        EntitlementGrant stored = await db
            .EntitlementGrants.AsNoTracking()
            .SingleAsync(g => g.Id == gift.Id);
        stored.ExpiresAt.Should().Be(now.UtcDateTime.AddDays(3), "ending stores this moment");
        stored.DeletedAt.Should().BeNull("the row stays as the record of what was given");
        (
            await db
                .IamAuditLogs.AsNoTracking()
                .CountAsync(a => a.Permission == "entitlement_grant:end")
        )
            .Should()
            .Be(1);
        await eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<BillingChangedEvent>(e =>
                    e.Area == "grant"
                    && e.Action == "ended"
                    && e.SubjectId == gift.Id
                    && e.TargetBroadcasterId == Broadcaster
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Ending_a_gift_of_another_channel_is_not_found_and_publishes_nothing()
    {
        (BillingTierChangeTestDbContext db, _, BillingTier proTier) =
            await SeedChannelOnBaseTierAsync();
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-29T12:00:00Z");
        FakeTimeProvider clock = new(now);
        IEventBus eventBus = Substitute.For<IEventBus>();
        AdminBillingController controller = BuildController(db, clock, out _, eventBus);
        EntitlementGrant gift = new()
        {
            BroadcasterId = Broadcaster,
            GrantedTierId = proTier.Id,
            Reason = "Two-month gift",
            ExpiresAt = now.UtcDateTime.AddMonths(2),
            IssuedAt = now.UtcDateTime,
            IssuedByAdminId = Actor,
        };
        db.EntitlementGrants.Add(gift);
        await db.SaveChangesAsync();

        IActionResult ended = await controller.EndGrant(
            Guid.Parse("0199c000-0000-7000-8000-000000000b99"),
            gift.Id,
            CancellationToken.None
        );

        ObjectResult objectResult = ended.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        EntitlementGrant stored = await db
            .EntitlementGrants.AsNoTracking()
            .SingleAsync(g => g.Id == gift.Id);
        stored.ExpiresAt.Should().Be(now.UtcDateTime.AddMonths(2), "the gift runs on untouched");
        await eventBus
            .DidNotReceive()
            .PublishAsync(Arg.Any<BillingChangedEvent>(), Arg.Any<CancellationToken>());
    }
}
