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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Billing;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// S-ADMIN-4c: proves invoices, dunning and refunds through <see cref="AdminBillingController"/> against a
/// REAL <see cref="SubscriptionService"/> (not a mock) — the list endpoint returns the actual persisted shape
/// and ordering, a refund PERSISTS the refunded amount and lands an <see cref="IamAuditLog"/> row, a second
/// refund of the same invoice is rejected rather than double-refunding, and the dunning state an unpaid
/// past-due invoice reports is computed by the exact same <c>Invoice.ResolveDunningStatus</c> the rest of the
/// system would call — never a decorative badge.
/// </summary>
public sealed class AdminBillingInvoicesTests
{
    private static readonly Guid Actor = Guid.Parse("0199c000-0000-7000-8000-000000000b01");
    private static readonly Guid Broadcaster = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static AdminBillingController BuildController(
        BillingTierChangeTestDbContext db,
        IStripeGateway? stripe = null
    )
    {
        IConfiguration config = Substitute.For<IConfiguration>();
        ISubscriptionService subscriptions = new SubscriptionService(
            db,
            new BillingTierService(db, TimeProvider.System),
            stripe ?? Substitute.For<IStripeGateway>(),
            config,
            Substitute.For<IEventBus>(),
            new FakeTimeProvider(Now)
        );
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(Actor.ToString());

        return new AdminBillingController(
            Substitute.For<IInviteCodeService>(),
            subscriptions,
            Substitute.For<IBillingTierAdminService>(),
            Substitute.For<IEntitlementGrantService>(),
            Substitute.For<IPricedUnitAdminService>(),
            currentUser
        );
    }

    private static async Task<(
        BillingTierChangeTestDbContext Db,
        Subscription Sub
    )> SeedChannelAsync()
    {
        BillingTierChangeTestDbContext db = BillingTierChangeTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                Name = "chan",
                NameNormalized = "chan",
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
            }
        );
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
        Subscription sub = new()
        {
            BroadcasterId = Broadcaster,
            TierId = tier.Id,
            Status = SubscriptionStatus.Active,
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();
        return (db, sub);
    }

    [Fact]
    public async Task ListInvoices_returns_the_real_shape_and_ordering()
    {
        (BillingTierChangeTestDbContext db, Subscription sub) = await SeedChannelAsync();
        db.Invoices.AddRange(
            new Invoice
            {
                BroadcasterId = Broadcaster,
                SubscriptionId = sub.Id,
                StripeInvoiceId = "in_old",
                Number = "INV-001",
                Status = InvoiceStatus.Paid,
                AmountDueCents = 500,
                AmountPaidCents = 500,
                Currency = "usd",
                IssuedAt = Now.UtcDateTime.AddDays(-30),
                PaidAt = Now.UtcDateTime.AddDays(-30),
            },
            new Invoice
            {
                BroadcasterId = Broadcaster,
                SubscriptionId = sub.Id,
                StripeInvoiceId = "in_new",
                Number = "INV-002",
                Status = InvoiceStatus.Open,
                AmountDueCents = 1_500,
                AmountPaidCents = 0,
                Currency = "usd",
                IssuedAt = Now.UtcDateTime,
                DueAt = Now.UtcDateTime.AddDays(-2),
            }
        );
        await db.SaveChangesAsync();
        AdminBillingController controller = BuildController(db);

        IActionResult result = await controller.ListInvoices(Broadcaster, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        StatusResponseDto<IReadOnlyList<InvoiceDto>> body =
            (StatusResponseDto<IReadOnlyList<InvoiceDto>>)((OkObjectResult)result).Value!;
        body.Data.Should().HaveCount(2);
        body.Data![0].Number.Should().Be("INV-002"); // newest issued first
        body.Data[0].Status.Should().Be("open");
        body.Data[0].AmountDueCents.Should().Be(1_500);
        body.Data[0].Currency.Should().Be("usd");
        body.Data[0].DunningStatus.Should().Be(nameof(InvoiceDunningStatus.PastDue));
        body.Data[1].Number.Should().Be("INV-001");
        body.Data[1].Status.Should().Be("paid");
        body.Data[1].AmountPaidCents.Should().Be(500);
        body.Data[1].PaidAt.Should().Be(Now.AddDays(-30));
        body.Data[1].DunningStatus.Should().Be(nameof(InvoiceDunningStatus.NotDunning));
    }

    [Fact]
    public async Task Refund_persists_the_refunded_amount_and_audits_the_actor()
    {
        (BillingTierChangeTestDbContext db, Subscription sub) = await SeedChannelAsync();
        Invoice invoice = new()
        {
            BroadcasterId = Broadcaster,
            SubscriptionId = sub.Id,
            StripeInvoiceId = "in_1",
            Status = InvoiceStatus.Paid,
            AmountDueCents = 1_999,
            AmountPaidCents = 1_999,
            Currency = "usd",
            IssuedAt = Now.UtcDateTime,
            PaidAt = Now.UtcDateTime,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        IStripeGateway gateway = Substitute.For<IStripeGateway>();
        gateway.RefundInvoiceAsync("in_1", Arg.Any<CancellationToken>()).Returns(Result.Success());
        AdminBillingController controller = BuildController(db, gateway);

        IActionResult result = await controller.RefundInvoice(invoice.Id, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        Invoice persisted = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        persisted.Status.Should().Be(InvoiceStatus.Refunded);
        persisted.AmountRefundedCents.Should().Be(1_999);
        persisted.RefundedAt.Should().NotBeNull();

        IamAuditLog audit = await db
            .IamAuditLogs.AsNoTracking()
            .SingleAsync(a => a.Permission == "invoice:refund");
        audit.PrincipalId.Should().Be(Actor);
        audit.TargetBroadcasterId.Should().Be(Broadcaster);
        audit.TargetResource.Should().Be(invoice.Id.ToString());
    }

    [Fact]
    public async Task Refund_a_second_time_is_rejected_never_double_refunding()
    {
        (BillingTierChangeTestDbContext db, Subscription sub) = await SeedChannelAsync();
        Invoice invoice = new()
        {
            BroadcasterId = Broadcaster,
            SubscriptionId = sub.Id,
            StripeInvoiceId = "in_2",
            Status = InvoiceStatus.Paid,
            AmountDueCents = 750,
            AmountPaidCents = 750,
            Currency = "usd",
            IssuedAt = Now.UtcDateTime,
            PaidAt = Now.UtcDateTime,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        IStripeGateway gateway = Substitute.For<IStripeGateway>();
        gateway.RefundInvoiceAsync("in_2", Arg.Any<CancellationToken>()).Returns(Result.Success());
        AdminBillingController controller = BuildController(db, gateway);

        IActionResult first = await controller.RefundInvoice(invoice.Id, CancellationToken.None);
        first.Should().BeOfType<OkObjectResult>();

        IActionResult second = await controller.RefundInvoice(invoice.Id, CancellationToken.None);

        second.Should().NotBeOfType<OkObjectResult>();
        await gateway.Received(1).RefundInvoiceAsync("in_2", Arg.Any<CancellationToken>());
        (await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id))
            .AmountRefundedCents.Should()
            .Be(750);
    }
}
