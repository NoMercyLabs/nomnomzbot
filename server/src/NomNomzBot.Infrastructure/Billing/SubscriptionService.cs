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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Billing.Enums;
using NomNomzBot.Domain.Billing.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// Subscription lifecycle (monetization-billing.md §3.1). The reads, the invite/admin <see cref="GrantTierAsync"/>,
/// the local cancel/resume, and the inbound Stripe webhook appliers are fully implemented and sync
/// <c>Channels.BillingTierKey</c>. Outbound hosted checkout + the self-serve billing portal go through
/// <see cref="IStripeGateway"/> (fail-closed to <c>SERVICE_UNAVAILABLE</c> when Stripe is unconfigured, so self-host
/// is unaffected). (Deferred — documented: proration tier change still routes through the portal; webhook
/// idempotency relies on the upsert being state-convergent pending a processed-event store.)
/// </summary>
public sealed class SubscriptionService(
    IApplicationDbContext db,
    IBillingTierService tiers,
    IStripeGateway stripe,
    IConfiguration configuration,
    IEventBus eventBus,
    TimeProvider clock
) : ISubscriptionService
{
    private const string StripeDeferred = "Stripe billing is not configured.";

    public async Task<Result<SubscriptionDto>> GetSubscriptionAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        bool channelExists = await db.Channels.AnyAsync(c => c.Id == broadcasterId, ct);
        if (!channelExists)
            return Result.Failure<SubscriptionDto>("Channel not found.", "NOT_FOUND");

        Subscription? sub = await FindAsync(broadcasterId, ct);
        return Result.Success(await ToDtoAsync(broadcasterId, sub, ct));
    }

    public async Task<Result<PagedList<InvoiceDto>>> ListInvoicesAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<Invoice> query = db.Invoices.Where(i =>
            i.BroadcasterId == broadcasterId && i.DeletedAt == null
        );
        int total = await query.CountAsync(ct);
        List<Invoice> rows = await query
            .OrderByDescending(i => i.IssuedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);
        return Result.Success(
            new PagedList<InvoiceDto>(
                [.. rows.Select(ToInvoiceDto)],
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<CheckoutSessionDto>> StartCheckoutAsync(
        Guid broadcasterId,
        StartCheckoutRequest request,
        CancellationToken ct = default
    )
    {
        BillingTier? tier = await db.BillingTiers.FirstOrDefaultAsync(
            t => t.Key == request.TierKey,
            ct
        );
        if (tier is null)
            return Result.Failure<CheckoutSessionDto>("Unknown billing tier.", "NOT_FOUND");
        if (string.IsNullOrWhiteSpace(tier.StripePriceId))
            return Result.Failure<CheckoutSessionDto>(
                "This tier is not purchasable.",
                "VALIDATION_FAILED"
            );

        string baseUrl = configuration["App:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
        return await stripe.CreateCheckoutSessionAsync(
            tier.StripePriceId,
            broadcasterId.ToString(),
            request.SuccessUrl ?? $"{baseUrl}/billing/success",
            request.CancelUrl ?? $"{baseUrl}/billing/cancel",
            ct
        );
    }

    public Task<Result<SubscriptionDto>> ChangeTierAsync(
        Guid broadcasterId,
        ChangeTierRequest request,
        CancellationToken ct = default
    ) => Task.FromResult(Result.Failure<SubscriptionDto>(StripeDeferred, "SERVICE_UNAVAILABLE"));

    public async Task<Result<SubscriptionDto>> CancelAsync(
        Guid broadcasterId,
        CancelSubscriptionRequest request,
        CancellationToken ct = default
    )
    {
        Subscription? sub = await FindAsync(broadcasterId, ct);
        if (sub is null)
            return Result.Failure<SubscriptionDto>("No active subscription.", "NOT_FOUND");

        DateTime now = clock.GetUtcNow().UtcDateTime;
        if (request.AtPeriodEnd)
        {
            sub.CancelAtPeriodEnd = true;
        }
        else
        {
            sub.Status = SubscriptionStatus.Canceled;
            sub.CanceledAt = now;
        }
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new SubscriptionCanceledEvent
            {
                BroadcasterId = broadcasterId,
                SubscriptionId = sub.Id,
                AtPeriodEnd = request.AtPeriodEnd,
                EffectiveAt = request.AtPeriodEnd
                    ? ToOffset(sub.CurrentPeriodEnd)
                    : new DateTimeOffset(now, TimeSpan.Zero),
            },
            ct
        );
        return Result.Success(await ToDtoAsync(broadcasterId, sub, ct));
    }

    public async Task<Result<SubscriptionDto>> ResumeAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Subscription? sub = await FindAsync(broadcasterId, ct);
        if (sub is null)
            return Result.Failure<SubscriptionDto>("No active subscription.", "NOT_FOUND");
        if (!sub.CancelAtPeriodEnd)
            return Result.Failure<SubscriptionDto>(
                "Subscription is not pending cancellation.",
                "VALIDATION_FAILED"
            );

        sub.CancelAtPeriodEnd = false;
        await db.SaveChangesAsync(ct);
        return Result.Success(await ToDtoAsync(broadcasterId, sub, ct));
    }

    public async Task<Result<BillingPortalDto>> CreateBillingPortalSessionAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Subscription? sub = await FindAsync(broadcasterId, ct);
        if (sub is null || string.IsNullOrWhiteSpace(sub.StripeSubscriptionId))
            return Result.Failure<BillingPortalDto>(
                "No active Stripe subscription to manage.",
                "NOT_FOUND"
            );

        string baseUrl = configuration["App:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
        return await stripe.CreateBillingPortalSessionAsync(
            sub.StripeSubscriptionId,
            $"{baseUrl}/billing",
            ct
        );
    }

    public async Task<Result> ApplyStripeSubscriptionEventAsync(
        StripeSubscriptionEventDto stripeEvent,
        CancellationToken ct = default
    )
    {
        Subscription? sub = await db.Subscriptions.FirstOrDefaultAsync(
            s => s.StripeSubscriptionId == stripeEvent.StripeSubscriptionId,
            ct
        );
        if (sub is null)
            return Result.Success(); // checkout-created sub not present yet — nothing to converge

        SubscriptionStatus from = sub.Status;
        sub.Status = ParseStatus(stripeEvent.Status);
        sub.CurrentPeriodStart = stripeEvent.CurrentPeriodStart?.UtcDateTime;
        sub.CurrentPeriodEnd = stripeEvent.CurrentPeriodEnd?.UtcDateTime;
        sub.TrialEndsAt = stripeEvent.TrialEnd?.UtcDateTime;
        sub.CancelAtPeriodEnd = stripeEvent.CancelAtPeriodEnd;
        await db.SaveChangesAsync(ct);

        if (from != sub.Status)
            await eventBus.PublishAsync(
                new SubscriptionStatusChangedEvent
                {
                    BroadcasterId = sub.BroadcasterId,
                    SubscriptionId = sub.Id,
                    FromStatus = StatusString(from),
                    ToStatus = StatusString(sub.Status),
                    TrialEndsAt = ToOffset(sub.TrialEndsAt),
                    GracePeriodEndsAt = ToOffset(sub.GracePeriodEndsAt),
                },
                ct
            );
        return Result.Success();
    }

    public async Task<Result> ApplyStripeInvoiceEventAsync(
        StripeInvoiceEventDto stripeEvent,
        CancellationToken ct = default
    )
    {
        Subscription? sub = await db.Subscriptions.FirstOrDefaultAsync(
            s => s.StripeSubscriptionId == stripeEvent.StripeSubscriptionId,
            ct
        );
        if (sub is null)
            return Result.Success(); // cannot resolve the tenant without the subscription

        Invoice? invoice = await db.Invoices.FirstOrDefaultAsync(
            i => i.StripeInvoiceId == stripeEvent.StripeInvoiceId,
            ct
        );
        InvoiceStatus status = ParseInvoiceStatus(stripeEvent.Status);
        if (invoice is null)
        {
            invoice = new Invoice
            {
                BroadcasterId = sub.BroadcasterId,
                SubscriptionId = sub.Id,
                StripeInvoiceId = stripeEvent.StripeInvoiceId,
                Number = stripeEvent.Number,
                Status = status,
                AmountDueCents = stripeEvent.AmountDueCents,
                AmountPaidCents = stripeEvent.AmountPaidCents,
                Currency = stripeEvent.Currency,
                PeriodStart = stripeEvent.PeriodStart?.UtcDateTime,
                PeriodEnd = stripeEvent.PeriodEnd?.UtcDateTime,
                HostedInvoiceUrl = stripeEvent.HostedInvoiceUrl,
                IssuedAt = stripeEvent.IssuedAt.UtcDateTime,
                PaidAt = stripeEvent.PaidAt?.UtcDateTime,
            };
            db.Invoices.Add(invoice);
        }
        else
        {
            invoice.Status = status;
            invoice.AmountPaidCents = stripeEvent.AmountPaidCents;
            invoice.PaidAt = stripeEvent.PaidAt?.UtcDateTime;
            invoice.HostedInvoiceUrl = stripeEvent.HostedInvoiceUrl;
        }
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new InvoicePaymentRecordedEvent
            {
                BroadcasterId = sub.BroadcasterId,
                InvoiceId = invoice.Id,
                Status = StringForInvoice(invoice.Status),
                AmountPaidCents = invoice.AmountPaidCents,
                Currency = invoice.Currency,
            },
            ct
        );
        return Result.Success();
    }

    public async Task<Result<SubscriptionDto>> GrantTierAsync(
        Guid broadcasterId,
        Guid tierId,
        bool isInviteOnlyGrant,
        CancellationToken ct = default
    )
    {
        BillingTier? tier = await db.BillingTiers.FirstOrDefaultAsync(
            t => t.Id == tierId && t.DeletedAt == null,
            ct
        );
        if (tier is null)
            return Result.Failure<SubscriptionDto>("Tier not found.", "NOT_FOUND");

        Subscription? sub = await FindAsync(broadcasterId, ct);
        string fromTierKey = "";
        if (sub is null)
        {
            sub = new Subscription { BroadcasterId = broadcasterId };
            db.Subscriptions.Add(sub);
        }
        else
        {
            BillingTier? old = await db.BillingTiers.FirstOrDefaultAsync(
                t => t.Id == sub.TierId,
                ct
            );
            fromTierKey = old?.Key ?? "";
        }
        sub.TierId = tierId;
        sub.Status = SubscriptionStatus.Active;
        sub.IsInviteOnlyGrant = isInviteOnlyGrant;

        await SyncChannelTierAsync(broadcasterId, tier.Key, ct);
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new SubscriptionTierChangedEvent
            {
                BroadcasterId = broadcasterId,
                SubscriptionId = sub.Id,
                FromTierKey = fromTierKey,
                ToTierKey = tier.Key,
                Status = StatusString(sub.Status),
                IsInviteOnlyGrant = isInviteOnlyGrant,
            },
            ct
        );
        return Result.Success(await ToDtoAsync(broadcasterId, sub, ct));
    }

    private Task<Subscription?> FindAsync(Guid broadcasterId, CancellationToken ct) =>
        db.Subscriptions.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.DeletedAt == null,
            ct
        );

    private async Task SyncChannelTierAsync(
        Guid broadcasterId,
        string tierKey,
        CancellationToken ct
    )
    {
        NomNomzBot.Domain.Identity.Entities.Channel? channel =
            await db.Channels.FirstOrDefaultAsync(c => c.Id == broadcasterId, ct);
        if (channel is not null)
            channel.BillingTierKey = tierKey;
    }

    private async Task<SubscriptionDto> ToDtoAsync(
        Guid broadcasterId,
        Subscription? sub,
        CancellationToken ct
    )
    {
        if (sub is not null)
        {
            BillingTier? tier = await db.BillingTiers.FirstOrDefaultAsync(
                t => t.Id == sub.TierId,
                ct
            );
            return new SubscriptionDto(
                sub.Id,
                broadcasterId,
                tier?.Key ?? "",
                tier?.DisplayName ?? "",
                StatusString(sub.Status),
                sub.CancelAtPeriodEnd,
                ToOffset(sub.CurrentPeriodEnd),
                ToOffset(sub.TrialEndsAt),
                ToOffset(sub.GracePeriodEndsAt),
                sub.IsInviteOnlyGrant,
                tier?.AllowsCustomBotName ?? false,
                tier?.PrioritySupport ?? false
            );
        }

        // Synthesized view from the resolved entitlement (free / self-host / unsubscribed base).
        EntitlementDto entitlement = (await tiers.GetEntitlementAsync(broadcasterId, ct)).Value;
        BillingTier? entTier = await db.BillingTiers.FirstOrDefaultAsync(
            t => t.Key == entitlement.TierKey,
            ct
        );
        return new SubscriptionDto(
            Guid.Empty,
            broadcasterId,
            entitlement.TierKey,
            entTier?.DisplayName ?? entitlement.TierKey,
            "active",
            false,
            null,
            null,
            null,
            false,
            entitlement.AllowsCustomBotName,
            entitlement.PrioritySupport
        );
    }

    private static InvoiceDto ToInvoiceDto(Invoice i) =>
        new(
            i.Id,
            i.Number,
            StringForInvoice(i.Status),
            i.AmountDueCents,
            i.AmountPaidCents,
            i.Currency,
            ToOffset(i.PeriodStart),
            ToOffset(i.PeriodEnd),
            new DateTimeOffset(i.IssuedAt, TimeSpan.Zero),
            ToOffset(i.PaidAt),
            i.HostedInvoiceUrl
        );

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value is DateTime d ? new DateTimeOffset(d, TimeSpan.Zero) : null;

    private static SubscriptionStatus ParseStatus(string status) =>
        status.ToLowerInvariant() switch
        {
            "active" => SubscriptionStatus.Active,
            "trialing" => SubscriptionStatus.Trialing,
            "past_due" => SubscriptionStatus.PastDue,
            "canceled" => SubscriptionStatus.Canceled,
            _ => SubscriptionStatus.Incomplete,
        };

    private static string StatusString(SubscriptionStatus status) =>
        status switch
        {
            SubscriptionStatus.Active => "active",
            SubscriptionStatus.Trialing => "trialing",
            SubscriptionStatus.PastDue => "past_due",
            SubscriptionStatus.Canceled => "canceled",
            _ => "incomplete",
        };

    private static InvoiceStatus ParseInvoiceStatus(string status) =>
        status.ToLowerInvariant() switch
        {
            "open" => InvoiceStatus.Open,
            "paid" => InvoiceStatus.Paid,
            "void" => InvoiceStatus.Void,
            "uncollectible" => InvoiceStatus.Uncollectible,
            _ => InvoiceStatus.Draft,
        };

    private static string StringForInvoice(InvoiceStatus status) =>
        status switch
        {
            InvoiceStatus.Open => "open",
            InvoiceStatus.Paid => "paid",
            InvoiceStatus.Void => "void",
            InvoiceStatus.Uncollectible => "uncollectible",
            _ => "draft",
        };
}
