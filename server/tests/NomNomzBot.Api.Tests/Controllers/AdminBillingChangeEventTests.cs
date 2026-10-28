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
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The operator billing desk reloads live: every successful billing write publishes one
/// <see cref="BillingChangedEvent"/> naming the area, the action and the subject; a failed write publishes nothing.
/// </summary>
public sealed class AdminBillingChangeEventTests
{
    private static readonly Guid Subject = Guid.Parse("0199c000-0000-7000-8000-000000000c01");
    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-000000000c02");

    private static (
        AdminBillingController Controller,
        IEventBus Bus,
        ISubscriptionService Subs,
        IInviteCodeService Invites
    ) Build()
    {
        IEventBus bus = Substitute.For<IEventBus>();
        ISubscriptionService subs = Substitute.For<ISubscriptionService>();
        IInviteCodeService invites = Substitute.For<IInviteCodeService>();
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(Guid.NewGuid().ToString());

        AdminBillingController controller = new(
            invites,
            subs,
            Substitute.For<IBillingTierAdminService>(),
            Substitute.For<IEntitlementGrantService>(),
            Substitute.For<IPricedUnitAdminService>(),
            currentUser,
            bus
        );

        return (controller, bus, subs, invites);
    }

    [Fact]
    public async Task Revoking_an_invite_publishes_one_invite_revoked_event_with_the_code_id()
    {
        (AdminBillingController controller, IEventBus bus, _, IInviteCodeService invites) = Build();
        invites
            .RevokeInviteCodeAsync(Subject, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await controller.RevokeInvite(Subject, CancellationToken.None);

        BillingChangedEvent published = (BillingChangedEvent)
            bus.ReceivedCalls().Single().GetArguments()[0]!;
        published.Area.Should().Be("invite");
        published.Action.Should().Be("revoked");
        published.SubjectId.Should().Be(Subject);
        published.TargetBroadcasterId.Should().BeNull();
    }

    [Fact]
    public async Task Granting_the_founders_badge_names_the_channel_it_touched()
    {
        (AdminBillingController controller, IEventBus bus, _, IInviteCodeService invites) = Build();
        invites
            .GrantFoundersBadgeAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new FoundersBadgeDto(Subject, DateTimeOffset.UtcNow, true, null))
            );

        await controller.GrantFounder(Channel, CancellationToken.None);

        BillingChangedEvent published = (BillingChangedEvent)
            bus.ReceivedCalls().Single().GetArguments()[0]!;
        published.Area.Should().Be("founder");
        published.TargetBroadcasterId.Should().Be(Channel);
    }

    [Fact]
    public async Task A_refund_that_fails_publishes_nothing()
    {
        (AdminBillingController controller, IEventBus bus, ISubscriptionService subs, _) = Build();
        subs.RefundInvoiceAsync(Subject, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<InvoiceDto>("already refunded", "VALIDATION_FAILED"));

        IActionResult response = await controller.RefundInvoice(Subject, CancellationToken.None);

        response.Should().NotBeOfType<OkObjectResult>();
        bus.ReceivedCalls().Should().BeEmpty();
    }
}
