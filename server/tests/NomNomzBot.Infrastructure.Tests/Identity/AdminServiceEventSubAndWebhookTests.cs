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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// S-ADMIN-6a — the first half of the 2am operator tools. Proves <see cref="AdminService"/>'s EventSub health
/// reads the REAL registry rows <c>TwitchEventSubHostedService</c> maintains (never a fabricated list) and
/// distinguishes tenants by their actual subscription state, and that its webhook delivery log + replay
/// genuinely append a new attempt, audit the acting operator, and refuse outright when the endpoint can no
/// longer receive a send.
/// </summary>
public sealed class AdminServiceEventSubAndWebhookTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);

    private static (
        AdminService Sut,
        AuthDbContext Db,
        IOutboundWebhookDispatcher Dispatcher
    ) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddHealthChecks();
        ServiceProvider provider = services.BuildServiceProvider();
        IOutboundWebhookDispatcher dispatcher = Substitute.For<IOutboundWebhookDispatcher>();

        AdminService sut = new(
            db,
            new FakeTimeProvider(Now),
            provider.GetRequiredService<HealthCheckService>(),
            Substitute.For<IPlatformBotReadinessGate>(),
            dispatcher,
            Substitute.For<IScheduledPipelineService>()
        );
        return (sut, db, dispatcher);
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

    private static EventSubSubscription SeedSubscription(
        AuthDbContext db,
        Guid broadcasterId,
        string status,
        string? lastError = null
    )
    {
        EventSubSubscription row = new()
        {
            BroadcasterId = broadcasterId,
            EventType = "channel.follow",
            Version = "2",
            Status = status,
            Enabled = status != "revoked",
            LastError = lastError,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.EventSubSubscriptions.Add(row);
        return row;
    }

    [Fact]
    public async Task GetEventSubHealth_distinguishes_a_healthy_tenant_from_a_revoked_one()
    {
        (AdminService sut, AuthDbContext db, _) = Build();
        Channel healthyTenant = SeedChannel(db, "healthy_streamer");
        Channel revokedTenant = SeedChannel(db, "revoked_streamer");
        SeedSubscription(db, healthyTenant.Id, "enabled");
        SeedSubscription(db, revokedTenant.Id, "revoked", lastError: "authorization revoked");
        await db.SaveChangesAsync();

        Result<PagedList<AdminEventSubTenantHealthDto>> result = await sut.GetEventSubHealthAsync(
            new PaginationParams(1, 25)
        );

        result.IsSuccess.Should().BeTrue();
        AdminEventSubTenantHealthDto healthy = result.Value.Items.Single(t =>
            t.BroadcasterId == healthyTenant.Id
        );
        AdminEventSubTenantHealthDto revoked = result.Value.Items.Single(t =>
            t.BroadcasterId == revokedTenant.Id
        );
        healthy.Topics.Single().Status.Should().Be("enabled");
        revoked.Topics.Single().Status.Should().Be("revoked");
        revoked.Topics.Single().LastError.Should().Be("authorization revoked");
    }

    [Fact]
    public async Task GetEventSubHealth_changes_when_the_underlying_subscription_state_changes()
    {
        (AdminService sut, AuthDbContext db, _) = Build();
        Channel tenant = SeedChannel(db, "flaky_streamer");
        EventSubSubscription row = SeedSubscription(db, tenant.Id, "enabled");
        await db.SaveChangesAsync();

        Result<PagedList<AdminEventSubTenantHealthDto>> before = await sut.GetEventSubHealthAsync(
            new PaginationParams(1, 25)
        );
        before.Value.Items.Single().Topics.Single().Status.Should().Be("enabled");

        // Twitch revokes the subscription (TwitchEventSubHostedService.OnRevocationAsync's real write shape:
        // Status flips, Enabled clears, LastError is set — no soft delete).
        row.Status = "revoked";
        row.Enabled = false;
        row.LastError = "user_removed";
        await db.SaveChangesAsync();

        Result<PagedList<AdminEventSubTenantHealthDto>> after = await sut.GetEventSubHealthAsync(
            new PaginationParams(1, 25)
        );

        after.Value.Items.Single().Topics.Single().Status.Should().Be("revoked");
        after.Value.Items.Single().Topics.Single().Enabled.Should().BeFalse();
    }

    private static (
        OutboundWebhookEndpoint Endpoint,
        OutboundWebhookDelivery Delivery
    ) SeedDelivery(AuthDbContext db, Guid broadcasterId)
    {
        OutboundWebhookEndpoint endpoint = new()
        {
            BroadcasterId = broadcasterId,
            Name = "ops-endpoint",
            Fqdn = "api.example.com",
            SigningSecretEnvelope = "sealed",
            EncryptionKeyId = Guid.NewGuid(),
            IsEnabled = true,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.OutboundWebhookEndpoints.Add(endpoint);

        OutboundWebhookDelivery delivery = new()
        {
            BroadcasterId = broadcasterId,
            EndpointId = endpoint.Id,
            WebhookMessageId = Guid.CreateVersion7(),
            EventType = "test.event",
            RenderedBody = "{}",
            Attempt = 1,
            Status = WebhookDeliveryStatus.Failed,
            ResponseCode = 500,
            CreatedAt = Now,
        };
        db.OutboundWebhookDeliveries.Add(delivery);
        return (endpoint, delivery);
    }

    [Fact]
    public async Task GetWebhookDeliveryLog_labels_a_delivery_whose_endpoint_was_since_deleted()
    {
        (AdminService sut, AuthDbContext db, _) = Build();
        Channel tenant = SeedChannel(db, "streamer_one");
        (OutboundWebhookEndpoint endpoint, OutboundWebhookDelivery delivery) = SeedDelivery(
            db,
            tenant.Id
        );
        await db.SaveChangesAsync();
        endpoint.DeletedAt = Now;
        await db.SaveChangesAsync();

        Result<PagedList<AdminWebhookDeliveryDto>> result = await sut.GetWebhookDeliveryLogAsync(
            new PaginationParams(1, 25)
        );

        AdminWebhookDeliveryDto dto = result.Value.Items.Single(d => d.Id == delivery.Id);
        dto.EndpointName.Should().Be("(deleted endpoint)");
        dto.EndpointCanReplay.Should().BeFalse();
    }

    [Fact]
    public async Task ReplayWebhookDelivery_appends_a_new_row_and_audits_the_acting_operator()
    {
        (AdminService sut, AuthDbContext db, IOutboundWebhookDispatcher dispatcher) = Build();
        Channel tenant = SeedChannel(db, "streamer_two");
        (_, OutboundWebhookDelivery original) = SeedDelivery(db, tenant.Id);
        await db.SaveChangesAsync();

        OutboundWebhookDelivery replayRow = new()
        {
            Id = 999,
            BroadcasterId = original.BroadcasterId,
            EndpointId = original.EndpointId,
            WebhookMessageId = Guid.CreateVersion7(),
            EventType = original.EventType,
            RenderedBody = original.RenderedBody,
            Attempt = 1,
            Status = WebhookDeliveryStatus.Delivered,
            ResponseCode = 200,
            CreatedAt = Now,
        };
        dispatcher
            .ReplayDeliveryAsync(Arg.Any<OutboundWebhookDelivery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(replayRow));

        Guid actorUserId = Guid.NewGuid();
        Result<AdminWebhookReplayResultDto> result = await sut.ReplayWebhookDeliveryAsync(
            original.Id,
            actorUserId
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.OriginalDeliveryId.Should().Be(original.Id);
        result.Value.NewDeliveryId.Should().Be(999);
        result.Value.Status.Should().Be("Delivered");

        IamAuditLog audit = db.IamAuditLogs.Single();
        audit.PrincipalId.Should().Be(actorUserId);
        audit.Permission.Should().Be("webhook:replay");
        audit.TargetBroadcasterId.Should().Be(original.BroadcasterId);
        audit.Justification.Should().Contain(actorUserId.ToString());
        audit.Justification.Should().Contain(original.Id.ToString());
        audit.Justification.Should().Contain("999");
    }

    [Fact]
    public async Task ReplayWebhookDelivery_is_rejected_and_writes_no_audit_entry_when_the_dispatcher_refuses()
    {
        (AdminService sut, AuthDbContext db, IOutboundWebhookDispatcher dispatcher) = Build();
        Channel tenant = SeedChannel(db, "streamer_three");
        (_, OutboundWebhookDelivery original) = SeedDelivery(db, tenant.Id);
        await db.SaveChangesAsync();

        dispatcher
            .ReplayDeliveryAsync(Arg.Any<OutboundWebhookDelivery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<OutboundWebhookDelivery>("Endpoint not found.", "NOT_FOUND"));

        Result<AdminWebhookReplayResultDto> result = await sut.ReplayWebhookDeliveryAsync(
            original.Id,
            Guid.NewGuid()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        db.IamAuditLogs.Should().BeEmpty();
        db.OutboundWebhookDeliveries.Count().Should().Be(1); // no new row was appended
    }

    [Fact]
    public async Task ReplayWebhookDelivery_fails_NOT_FOUND_for_an_unknown_delivery_id()
    {
        (AdminService sut, _, _) = Build();

        Result<AdminWebhookReplayResultDto> result = await sut.ReplayWebhookDeliveryAsync(
            123456,
            Guid.NewGuid()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
