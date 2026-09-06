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
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Billing.Enums;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Billing;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// S-ADMIN-4a: proves tier authoring through <see cref="AdminBillingController"/> — an edit PERSISTS (not
/// merely a 200) and lands an <see cref="IamAuditLog"/> row; the preview endpoint returns the REAL counted
/// number of tenants on a tier; and an update carrying a stale confirmed count is rejected (fails closed)
/// rather than applied, mirroring the platform-content publish-preview contract
/// (<c>PlatformContentService.PublishAsync</c>'s <c>PREVIEW_STALE</c> gate).
/// </summary>
public sealed class AdminBillingTierEditingTests
{
    private static readonly Guid Actor = Guid.Parse("0199b000-0000-7000-8000-000000000a01");

    private static AdminBillingController BuildController(
        BillingTierChangeTestDbContext db,
        out IBillingTierAdminService tierAdmin
    )
    {
        tierAdmin = new BillingTierAdminService(db, TimeProvider.System);
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(Actor.ToString());

        return new AdminBillingController(
            Substitute.For<IInviteCodeService>(),
            Substitute.For<ISubscriptionService>(),
            tierAdmin,
            Substitute.For<IEntitlementGrantService>(),
            Substitute.For<IPricedUnitAdminService>(),
            currentUser
        );
    }

    private static async Task<(
        BillingTierChangeTestDbContext Db,
        BillingTier Tier
    )> SeedTierWithTenantsAsync(int tenantCount)
    {
        BillingTierChangeTestDbContext db = BillingTierChangeTestDbContext.New();

        BillingTier tier = new()
        {
            Key = "pro",
            DisplayName = "Pro",
            PriceCents = 999,
            Currency = "usd",
            IsPublic = true,
            SortOrder = 1,
        };
        db.BillingTiers.Add(tier);

        for (int i = 0; i < tenantCount; i++)
        {
            Guid broadcasterId = Guid.CreateVersion7();
            db.Channels.Add(
                new()
                {
                    Id = broadcasterId,
                    Name = $"tenant{i}",
                    NameNormalized = $"tenant{i}",
                    DeploymentMode = AuthEnums.DeploymentMode.Saas,
                }
            );
            db.Subscriptions.Add(
                new()
                {
                    BroadcasterId = broadcasterId,
                    TierId = tier.Id,
                    Status = SubscriptionStatus.Active,
                }
            );
        }

        await db.SaveChangesAsync();
        return (db, tier);
    }

    [Fact]
    public async Task Updating_a_tier_persists_the_change_and_writes_an_audit_entry()
    {
        (BillingTierChangeTestDbContext db, BillingTier tier) = await SeedTierWithTenantsAsync(0);
        AdminBillingController controller = BuildController(db, out _);

        UpdateTierRequest request = new(
            DisplayName: "Pro (renamed)",
            PriceCents: 1499,
            Currency: "usd",
            AllowsCustomBotName: true,
            PrioritySupport: true,
            IsPublic: true,
            SortOrder: 1,
            Limits: [new TierLimitDto("custom_commands", 500)],
            ConfirmedAffectedTenantCount: 0
        );

        IActionResult result = await controller.UpdateTier(
            tier.Id,
            request,
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();

        // The persisted state — read back from a FRESH query, never the tracked entity the action mutated —
        // is what proves the write actually landed, not merely that the endpoint answered 200.
        BillingTier persisted = await db
            .BillingTiers.AsNoTracking()
            .SingleAsync(t => t.Id == tier.Id);
        persisted.DisplayName.Should().Be("Pro (renamed)");
        persisted.PriceCents.Should().Be(1499);
        persisted.AllowsCustomBotName.Should().BeTrue();
        persisted.PrioritySupport.Should().BeTrue();

        TierLimit persistedLimit = await db
            .TierLimits.AsNoTracking()
            .SingleAsync(l => l.TierId == tier.Id);
        persistedLimit.LimitKey.Should().Be("custom_commands");
        persistedLimit.LimitValue.Should().Be(500);

        IamAuditLog auditEntry = await db
            .IamAuditLogs.AsNoTracking()
            .SingleAsync(a => a.TargetResource == "pro" && a.Permission == "billing_tier:update");
        auditEntry.PrincipalId.Should().Be(Actor);
        auditEntry.AffectedTenantCount.Should().Be(0);
        auditEntry.Justification.Should().Contain("1499");
    }

    [Fact]
    public async Task Preview_returns_the_real_counted_number_of_tenants_on_the_tier()
    {
        (BillingTierChangeTestDbContext db, BillingTier tier) = await SeedTierWithTenantsAsync(3);
        AdminBillingController controller = BuildController(
            db,
            out IBillingTierAdminService tierAdmin
        );

        Result<TierChangePreviewDto> preview = await tierAdmin.PreviewTierChangeAsync(
            tier.Id,
            CancellationToken.None
        );

        preview.IsSuccess.Should().BeTrue();
        preview.Value.AffectedTenantCount.Should().Be(3);
        preview.Value.SampleChannelNames.Should().HaveCount(3);
    }

    [Fact]
    public async Task Update_with_a_stale_confirmed_count_fails_closed_and_does_not_apply()
    {
        (BillingTierChangeTestDbContext db, BillingTier tier) = await SeedTierWithTenantsAsync(3);
        AdminBillingController controller = BuildController(db, out _);

        // The caller previewed at 3, but a 4th tenant subscribed to the tier in the meantime — the
        // confirmed count the client is about to send no longer matches reality.
        Guid latecomer = Guid.CreateVersion7();
        db.Channels.Add(
            new()
            {
                Id = latecomer,
                Name = "latecomer",
                NameNormalized = "latecomer",
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
            }
        );
        db.Subscriptions.Add(
            new()
            {
                BroadcasterId = latecomer,
                TierId = tier.Id,
                Status = SubscriptionStatus.Active,
            }
        );
        await db.SaveChangesAsync();

        UpdateTierRequest staleRequest = new(
            DisplayName: "Pro (should not apply)",
            PriceCents: 1,
            Currency: "usd",
            AllowsCustomBotName: false,
            PrioritySupport: false,
            IsPublic: true,
            SortOrder: 1,
            Limits: [],
            ConfirmedAffectedTenantCount: 3 // stale — reality is now 4
        );

        IActionResult result = await controller.UpdateTier(
            tier.Id,
            staleRequest,
            CancellationToken.None
        );

        result.Should().BeOfType<ConflictObjectResult>();
        ConflictObjectResult conflict = (ConflictObjectResult)result;
        StatusResponseDto<object> body = (StatusResponseDto<object>)conflict.Value!;
        body.Code.Should().Be("PREVIEW_STALE");

        // Fails CLOSED: the tier's price and name are untouched, and no audit entry was written for this
        // rejected attempt.
        BillingTier persisted = await db
            .BillingTiers.AsNoTracking()
            .SingleAsync(t => t.Id == tier.Id);
        persisted.DisplayName.Should().Be("Pro");
        persisted.PriceCents.Should().Be(999);

        bool anyUpdateAudited = await db.IamAuditLogs.AnyAsync(a =>
            a.TargetResource == "pro" && a.Permission == "billing_tier:update"
        );
        anyUpdateAudited.Should().BeFalse();
    }
}
