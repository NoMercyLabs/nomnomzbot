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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Proves the retry drain (webhooks.md §3.7): it re-attempts a failed delivery whose NextRetryAt is now due (bumping
/// the attempt), but leaves a not-yet-due delivery and an already-delivered one untouched.
/// </summary>
public sealed class WebhookRetryProcessorTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e01");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (
        WebhookRetryProcessor Sut,
        AuthDbContext Db,
        IOutboundWebhookDispatcher Dispatcher
    ) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IOutboundWebhookDispatcher dispatcher = Substitute.For<IOutboundWebhookDispatcher>();
        dispatcher
            .AttemptDeliveryAsync(Arg.Any<OutboundWebhookDelivery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(WebhookDeliveryStatus.Delivered));
        return (
            new WebhookRetryProcessor(db, dispatcher, new FakeTimeProvider(Now)),
            db,
            dispatcher
        );
    }

    private static async Task SeedAsync(
        AuthDbContext db,
        WebhookDeliveryStatus status,
        DateTime? nextRetryAt
    )
    {
        db.OutboundWebhookDeliveries.Add(
            new OutboundWebhookDelivery
            {
                BroadcasterId = Channel,
                EndpointId = Guid.Parse("0192a000-0000-7000-8000-0000000000ee"),
                WebhookMessageId = Guid.Parse("0192a000-0000-7000-8000-0000000000ef"),
                EventType = "test.event",
                RenderedBody = "{}",
                Attempt = 1,
                Status = status,
                NextRetryAt = nextRetryAt,
                CreatedAt = Now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Re_attempts_a_due_failed_delivery_and_bumps_the_attempt()
    {
        (WebhookRetryProcessor sut, AuthDbContext db, IOutboundWebhookDispatcher dispatcher) =
            Build();
        await SeedAsync(db, WebhookDeliveryStatus.Failed, Now.UtcDateTime.AddMinutes(-1));

        int processed = await sut.ProcessDueAsync(50);

        processed.Should().Be(1);
        await dispatcher
            .Received()
            .AttemptDeliveryAsync(Arg.Any<OutboundWebhookDelivery>(), Arg.Any<CancellationToken>());
        db.OutboundWebhookDeliveries.Single().Attempt.Should().Be(2);
    }

    [Fact]
    public async Task Leaves_a_not_yet_due_delivery_untouched()
    {
        (WebhookRetryProcessor sut, AuthDbContext db, IOutboundWebhookDispatcher dispatcher) =
            Build();
        await SeedAsync(db, WebhookDeliveryStatus.Failed, Now.UtcDateTime.AddMinutes(10));

        (await sut.ProcessDueAsync(50)).Should().Be(0);
        await dispatcher
            .DidNotReceive()
            .AttemptDeliveryAsync(Arg.Any<OutboundWebhookDelivery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_an_already_delivered_delivery()
    {
        (WebhookRetryProcessor sut, AuthDbContext db, _) = Build();
        await SeedAsync(db, WebhookDeliveryStatus.Delivered, null);

        (await sut.ProcessDueAsync(50)).Should().Be(0);
    }
}
