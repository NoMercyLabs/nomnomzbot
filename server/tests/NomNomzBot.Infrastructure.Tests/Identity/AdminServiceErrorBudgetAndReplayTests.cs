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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// S-ADMIN-6c — the last two 2am tools. Proves the error budget is computed purely from real recorded
/// <c>OutboundWebhookDelivery</c> outcomes (never a fabricated percentage) and never lets one tenant's
/// failures count toward another's; and that the event-store replay tool previews the REAL count a scope
/// would replay, fails closed against a stale count, actually re-applies events to a projection idempotently,
/// and audits the operator, scope, and count.
/// </summary>
public sealed class AdminServiceErrorBudgetAndReplayTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    private static (AdminService Sut, AuthDbContext Db, FakeTimeProvider Clock) Build(
        params IProjection[] projections
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        FakeTimeProvider clock = new(Now);
        ServiceCollection services = new();
        services.AddLogging();
        services.AddHealthChecks();
        ServiceProvider provider = services.BuildServiceProvider();

        AdminService sut = new(
            db,
            clock,
            provider.GetRequiredService<HealthCheckService>(),
            Substitute.For<IPlatformBotReadinessGate>(),
            Substitute.For<IOutboundWebhookDispatcher>(),
            Substitute.For<IScheduledPipelineService>(),
            projections,
            new EventUpcasterRegistry([])
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
                CreatedAt = Now,
                UpdatedAt = Now,
            }
        );
        Channel channel = new()
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerId,
            Name = login,
            NameNormalized = login.ToLowerInvariant(),
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.Channels.Add(channel);
        return channel;
    }

    private static Guid SeedEndpoint(AuthDbContext db, Guid broadcasterId)
    {
        OutboundWebhookEndpoint endpoint = new()
        {
            BroadcasterId = broadcasterId,
            Name = "budget-endpoint",
            Fqdn = "api.example.com",
            SigningSecretEnvelope = "sealed",
            EncryptionKeyId = Guid.NewGuid(),
            IsEnabled = true,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.OutboundWebhookEndpoints.Add(endpoint);
        return endpoint.Id;
    }

    private static void SeedDelivery(
        AuthDbContext db,
        Guid broadcasterId,
        Guid endpointId,
        WebhookDeliveryStatus status,
        DateTime createdAt
    )
    {
        db.OutboundWebhookDeliveries.Add(
            new OutboundWebhookDelivery
            {
                BroadcasterId = broadcasterId,
                EndpointId = endpointId,
                WebhookMessageId = Guid.CreateVersion7(),
                EventType = "test.event",
                RenderedBody = "{}",
                Attempt = 1,
                Status = status,
                ResponseCode = status == WebhookDeliveryStatus.Delivered ? 200 : 500,
                CreatedAt = createdAt,
            }
        );
    }

    // ── Error budget ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetErrorBudget_is_computed_from_real_delivery_outcomes_and_changes_when_they_change()
    {
        (AdminService sut, AuthDbContext db, _) = Build();
        Channel tenant = SeedChannel(db, "streamer_budget");
        Guid endpointId = SeedEndpoint(db, tenant.Id);

        // Real recorded outcomes inside the trailing-24h window: 3 delivered, 1 failed.
        SeedDelivery(db, tenant.Id, endpointId, WebhookDeliveryStatus.Delivered, Now.AddHours(-1));
        SeedDelivery(db, tenant.Id, endpointId, WebhookDeliveryStatus.Delivered, Now.AddHours(-2));
        SeedDelivery(db, tenant.Id, endpointId, WebhookDeliveryStatus.Delivered, Now.AddHours(-3));
        SeedDelivery(db, tenant.Id, endpointId, WebhookDeliveryStatus.Failed, Now.AddHours(-4));
        // Outside the window and still in-flight — must never count.
        SeedDelivery(db, tenant.Id, endpointId, WebhookDeliveryStatus.Failed, Now.AddHours(-30));
        SeedDelivery(db, tenant.Id, endpointId, WebhookDeliveryStatus.Pending, Now.AddHours(-1));
        await db.SaveChangesAsync();

        Result<PagedList<AdminTenantErrorBudgetDto>> before = await sut.GetErrorBudgetAsync(
            new PaginationParams()
        );

        before.IsSuccess.Should().BeTrue();
        AdminTenantErrorBudgetDto budget = before.Value.Items.Single(b =>
            b.BroadcasterId == tenant.Id
        );
        budget.Attempts.Should().Be(4); // the stale + pending rows are excluded
        budget.Errors.Should().Be(1);
        budget.ErrorRate.Should().Be(0.25);
        budget.TargetSuccessRate.Should().Be(0.99);
        // allowed error rate = 1%; actual = 25% -> budget is deeply exhausted (negative remaining).
        budget.BudgetRemainingFraction.Should().BeApproximately(1 - (0.25 / 0.01), 0.000001);

        // The underlying records change — one more real failure lands in the window.
        SeedDelivery(db, tenant.Id, endpointId, WebhookDeliveryStatus.Failed, Now.AddHours(-1));
        await db.SaveChangesAsync();

        Result<PagedList<AdminTenantErrorBudgetDto>> after = await sut.GetErrorBudgetAsync(
            new PaginationParams()
        );
        AdminTenantErrorBudgetDto updated = after.Value.Items.Single(b =>
            b.BroadcasterId == tenant.Id
        );
        updated.Attempts.Should().Be(5);
        updated.Errors.Should().Be(2);
        updated.ErrorRate.Should().Be(0.4);
        updated.ErrorRate.Should().NotBe(budget.ErrorRate); // the figure genuinely moved
    }

    [Fact]
    public async Task GetErrorBudget_never_lets_one_tenant_s_failures_count_toward_another_s()
    {
        (AdminService sut, AuthDbContext db, _) = Build();
        Channel brokenTenant = SeedChannel(db, "streamer_broken_integration");
        Channel healthyTenant = SeedChannel(db, "streamer_healthy_integration");
        Guid brokenEndpoint = SeedEndpoint(db, brokenTenant.Id);
        Guid healthyEndpoint = SeedEndpoint(db, healthyTenant.Id);

        SeedDelivery(
            db,
            brokenTenant.Id,
            brokenEndpoint,
            WebhookDeliveryStatus.Failed,
            Now.AddHours(-1)
        );
        SeedDelivery(
            db,
            brokenTenant.Id,
            brokenEndpoint,
            WebhookDeliveryStatus.DeadLetter,
            Now.AddHours(-2)
        );

        SeedDelivery(
            db,
            healthyTenant.Id,
            healthyEndpoint,
            WebhookDeliveryStatus.Delivered,
            Now.AddHours(-1)
        );
        SeedDelivery(
            db,
            healthyTenant.Id,
            healthyEndpoint,
            WebhookDeliveryStatus.Delivered,
            Now.AddHours(-2)
        );
        SeedDelivery(
            db,
            healthyTenant.Id,
            healthyEndpoint,
            WebhookDeliveryStatus.Delivered,
            Now.AddHours(-3)
        );
        await db.SaveChangesAsync();

        Result<PagedList<AdminTenantErrorBudgetDto>> result = await sut.GetErrorBudgetAsync(
            new PaginationParams()
        );

        AdminTenantErrorBudgetDto broken = result.Value.Items.Single(b =>
            b.BroadcasterId == brokenTenant.Id
        );
        AdminTenantErrorBudgetDto healthy = result.Value.Items.Single(b =>
            b.BroadcasterId == healthyTenant.Id
        );

        broken.Attempts.Should().Be(2);
        broken.Errors.Should().Be(2);
        broken.ErrorRate.Should().Be(1.0);

        // The healthy tenant's figure is entirely untouched by the broken tenant's failures.
        healthy.Attempts.Should().Be(3);
        healthy.Errors.Should().Be(0);
        healthy.ErrorRate.Should().Be(0.0);
    }

    // ── Event-store replay ───────────────────────────────────────────────────

    /// <summary>A minimal real <see cref="IProjection"/> double: its <c>ApplyAsync</c> upserts keyed on
    /// <c>EventId</c> exactly as the real contract requires ("MUST be idempotent... never a blind insert"),
    /// so a test can prove the admin replay tool relies on that contract rather than inventing its own.</summary>
    private sealed class FakeProjection(string name, params string[] subscribedEventTypes)
        : IProjection
    {
        public string Name { get; } = name;
        public bool IsGlobal => false;
        public IReadOnlySet<string> SubscribedEventTypes { get; } =
            subscribedEventTypes.ToHashSet();

        /// <summary>Keyed on EventId — an upsert, not a list — so re-applying the same event is a no-op on
        /// the resulting shape, exactly like a real projection's read-model row.</summary>
        public Dictionary<Guid, EventRecord> State { get; } = [];

        public Task<Result> ApplyAsync(
            EventRecord @event,
            CancellationToken cancellationToken = default
        )
        {
            State[@event.EventId] = @event;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> ResetAsync(
            Guid? broadcasterId,
            CancellationToken cancellationToken = default
        )
        {
            State.Clear();
            return Task.FromResult(Result.Success());
        }
    }

    private static EventJournal SeedEvent(
        AuthDbContext db,
        Guid broadcasterId,
        string eventType,
        DateTime occurredAt,
        long streamPosition
    )
    {
        EventJournal row = new()
        {
            EventId = Guid.NewGuid(),
            BroadcasterId = broadcasterId,
            StreamPosition = streamPosition,
            EventType = eventType,
            EventVersion = 1,
            Source = "domain",
            Payload = "{}",
            PayloadIsEncrypted = false,
            Metadata = "{}",
            OccurredAt = occurredAt,
            RecordedAt = occurredAt,
        };
        db.EventJournals.Add(row);
        return row;
    }

    [Fact]
    public async Task PreviewEventReplay_returns_the_real_matching_count_scoped_to_the_projection_s_own_subscribed_types()
    {
        FakeProjection projection = new("fake-currency-balance", "currency.credited");
        (AdminService sut, AuthDbContext db, _) = Build(projection);
        Channel tenant = SeedChannel(db, "streamer_replay_preview");

        SeedEvent(db, tenant.Id, "currency.credited", Now.AddHours(-1), 1);
        SeedEvent(db, tenant.Id, "currency.credited", Now.AddHours(-2), 2);
        SeedEvent(db, tenant.Id, "currency.credited", Now.AddHours(-3), 3);
        // A different event type in the SAME window — the projection does not subscribe to it, so it must
        // never inflate the count the operator is about to confirm.
        SeedEvent(db, tenant.Id, "chat.message", Now.AddHours(-1), 4);
        await db.SaveChangesAsync();

        Result<AdminEventReplayPreviewDto> preview = await sut.PreviewEventReplayAsync(
            tenant.Id,
            "fake-currency-balance",
            Now.AddDays(-1),
            Now,
            eventType: null
        );

        preview.IsSuccess.Should().BeTrue();
        preview.Value.MatchingEventCount.Should().Be(3);

        // An explicit event type the projection does NOT subscribe to is refused up front, before any
        // count is even shown, rather than quietly promising to replay events it cannot handle.
        Result<AdminEventReplayPreviewDto> mismatched = await sut.PreviewEventReplayAsync(
            tenant.Id,
            "fake-currency-balance",
            Now.AddDays(-1),
            Now,
            eventType: "chat.message"
        );
        mismatched.IsFailure.Should().BeTrue();
        mismatched.ErrorCode.Should().Be("EVENT_TYPE_NOT_SUBSCRIBED");
    }

    [Fact]
    public async Task ExecuteEventReplay_with_a_stale_count_fails_closed_and_applies_nothing()
    {
        FakeProjection projection = new("fake-stale-check", "currency.credited");
        (AdminService sut, AuthDbContext db, _) = Build(projection);
        Channel tenant = SeedChannel(db, "streamer_stale_replay");
        SeedEvent(db, tenant.Id, "currency.credited", Now.AddHours(-1), 1);
        SeedEvent(db, tenant.Id, "currency.credited", Now.AddHours(-2), 2);
        await db.SaveChangesAsync();

        Result<AdminEventReplayPreviewDto> preview = await sut.PreviewEventReplayAsync(
            tenant.Id,
            "fake-stale-check",
            Now.AddDays(-1),
            Now,
            eventType: null
        );
        long shownCount = preview.Value.MatchingEventCount; // 2 — what the operator actually saw

        // A THIRD event lands after the preview was shown but before the operator confirms — the number
        // the operator is about to act on is no longer the real number.
        SeedEvent(db, tenant.Id, "currency.credited", Now.AddMinutes(-5), 3);
        await db.SaveChangesAsync();

        Result<AdminEventReplayResultDto> execute = await sut.ExecuteEventReplayAsync(
            tenant.Id,
            "fake-stale-check",
            Now.AddDays(-1),
            Now,
            eventType: null,
            expectedCount: shownCount,
            actorUserId: Guid.NewGuid()
        );

        execute.IsFailure.Should().BeTrue();
        execute.ErrorCode.Should().Be("STALE_COUNT");
        // Refused BEFORE anything ran — the projection's state is untouched, and nothing was audited.
        projection.State.Should().BeEmpty();
        db.IamAuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteEventReplay_applies_the_scoped_events_and_is_idempotent_on_a_second_run()
    {
        FakeProjection projection = new("fake-idempotent-replay", "currency.credited");
        (AdminService sut, AuthDbContext db, _) = Build(projection);
        Channel tenant = SeedChannel(db, "streamer_idempotent_replay");
        EventJournal first = SeedEvent(db, tenant.Id, "currency.credited", Now.AddHours(-1), 1);
        EventJournal second = SeedEvent(db, tenant.Id, "currency.credited", Now.AddHours(-2), 2);
        await db.SaveChangesAsync();

        Result<AdminEventReplayResultDto> firstRun = await sut.ExecuteEventReplayAsync(
            tenant.Id,
            "fake-idempotent-replay",
            Now.AddDays(-1),
            Now,
            eventType: null,
            expectedCount: 2,
            actorUserId: Guid.NewGuid()
        );

        firstRun.IsSuccess.Should().BeTrue();
        firstRun.Value.AppliedCount.Should().Be(2);
        // The events genuinely reached the projection's real ApplyAsync.
        projection.State.Should().ContainKey(first.EventId);
        projection.State.Should().ContainKey(second.EventId);
        projection.State.Should().HaveCount(2);

        // Running the SAME scope again (nothing new landed, so the fresh count still matches) re-applies
        // the same events. Because ApplyAsync is contractually an upsert keyed on EventId (never a blind
        // insert), the resulting state does NOT double up.
        Result<AdminEventReplayResultDto> secondRun = await sut.ExecuteEventReplayAsync(
            tenant.Id,
            "fake-idempotent-replay",
            Now.AddDays(-1),
            Now,
            eventType: null,
            expectedCount: 2,
            actorUserId: Guid.NewGuid()
        );

        secondRun.IsSuccess.Should().BeTrue();
        secondRun.Value.AppliedCount.Should().Be(2);
        projection.State.Should().HaveCount(2); // still 2, not 4 — no silent double-apply
    }

    [Fact]
    public async Task ExecuteEventReplay_audits_the_operator_the_scope_and_the_count()
    {
        FakeProjection projection = new("fake-audited-replay", "currency.credited");
        (AdminService sut, AuthDbContext db, _) = Build(projection);
        Channel tenant = SeedChannel(db, "streamer_audited_replay");
        SeedEvent(db, tenant.Id, "currency.credited", Now.AddHours(-1), 1);
        SeedEvent(db, tenant.Id, "currency.credited", Now.AddHours(-2), 2);
        SeedEvent(db, tenant.Id, "currency.credited", Now.AddHours(-3), 3);
        await db.SaveChangesAsync();

        Guid actorUserId = Guid.NewGuid();
        Result<AdminEventReplayResultDto> result = await sut.ExecuteEventReplayAsync(
            tenant.Id,
            "fake-audited-replay",
            Now.AddDays(-1),
            Now,
            eventType: "currency.credited",
            expectedCount: 3,
            actorUserId: actorUserId
        );

        result.IsSuccess.Should().BeTrue();

        IamAuditLog audit = db.IamAuditLogs.Single();
        audit.PrincipalId.Should().Be(actorUserId);
        audit.Permission.Should().Be("eventstore:admin-replay");
        audit.TargetBroadcasterId.Should().Be(tenant.Id);
        audit.TargetResource.Should().Be("fake-audited-replay");
        audit.Justification.Should().Contain(actorUserId.ToString());
        audit.Justification.Should().Contain("fake-audited-replay");
        audit.Justification.Should().Contain("count=3");
    }
}
