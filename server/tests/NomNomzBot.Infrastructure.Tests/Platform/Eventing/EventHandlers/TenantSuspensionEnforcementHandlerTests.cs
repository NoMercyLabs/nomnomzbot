// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Platform.Eventing.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.EventHandlers;

/// <summary>
/// Proves S088's live enforcement leg: a suspend/platform-ban revokes THAT tenant's EventSub session
/// immediately (bot parted, no chat reads or posts) and a reinstate re-subscribes it — scoped to exactly the
/// affected tenant, never any other broadcaster sharing the bot's working set.
/// </summary>
public sealed class TenantSuspensionEnforcementHandlerTests
{
    private static readonly Guid SuspendedTenant = Guid.Parse(
        "0192a000-0000-7000-8000-0000000000c1"
    );
    private static readonly Guid OtherTenant = Guid.Parse("0192a000-0000-7000-8000-0000000000c2");
    private static readonly Guid Principal = Guid.Parse("0192a000-0000-7000-8000-0000000000c3");

    private static (
        TenantSuspensionEnforcementHandler Handler,
        ITwitchEventSubService EventSub
    ) Build()
    {
        ITwitchEventSubService eventSub = Substitute.For<ITwitchEventSubService>();
        eventSub
            .UnsubscribeAllAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        eventSub
            .EnsureSubscribedAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        TenantSuspensionEnforcementHandler handler = new(
            eventSub,
            NullLogger<TenantSuspensionEnforcementHandler>.Instance
        );
        return (handler, eventSub);
    }

    private static TenantSuspensionChangedEvent Event(Guid target, string newStatus) =>
        new()
        {
            BroadcasterId = Guid.Empty,
            PrincipalId = Principal,
            TargetBroadcasterId = target,
            NewStatus = newStatus,
        };

    [Fact]
    public async Task Suspending_a_tenant_revokes_exactly_that_tenants_EventSub_session()
    {
        (TenantSuspensionEnforcementHandler handler, ITwitchEventSubService eventSub) = Build();

        await handler.HandleAsync(Event(SuspendedTenant, AuthEnums.ChannelStatus.Suspended));

        // The side effect: THAT tenant's whole subscription set (chat read included, so the bot stops
        // reacting to and posting in the channel) is torn down.
        await eventSub
            .Received(1)
            .UnsubscribeAllAsync(SuspendedTenant, Arg.Any<CancellationToken>());
        // Another tenant's session is never touched by a suspend on a different broadcaster.
        await eventSub
            .DidNotReceive()
            .UnsubscribeAllAsync(OtherTenant, Arg.Any<CancellationToken>());
        await eventSub
            .DidNotReceive()
            .EnsureSubscribedAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PlatformBanning_a_tenant_also_revokes_its_EventSub_session()
    {
        (TenantSuspensionEnforcementHandler handler, ITwitchEventSubService eventSub) = Build();

        await handler.HandleAsync(Event(SuspendedTenant, AuthEnums.ChannelStatus.PlatformBanned));

        await eventSub
            .Received(1)
            .UnsubscribeAllAsync(SuspendedTenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reinstating_a_tenant_resubscribes_exactly_that_tenants_full_topic_set()
    {
        (TenantSuspensionEnforcementHandler handler, ITwitchEventSubService eventSub) = Build();

        await handler.HandleAsync(Event(SuspendedTenant, AuthEnums.ChannelStatus.Active));

        // The side effect: the bot resumes for THAT tenant — re-subscribed to its non-empty topic set.
        await eventSub
            .Received(1)
            .EnsureSubscribedAsync(
                SuspendedTenant,
                Arg.Is<IReadOnlyCollection<string>>(topics => topics.Count > 0),
                Arg.Any<CancellationToken>()
            );
        // Another tenant's session is untouched by a reinstate on a different broadcaster.
        await eventSub
            .DidNotReceive()
            .EnsureSubscribedAsync(
                OtherTenant,
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            );
        await eventSub
            .DidNotReceive()
            .UnsubscribeAllAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_platform_scoped_event_with_no_target_tenant_is_ignored()
    {
        (TenantSuspensionEnforcementHandler handler, ITwitchEventSubService eventSub) = Build();

        await handler.HandleAsync(Event(Guid.Empty, AuthEnums.ChannelStatus.Suspended));

        await eventSub
            .DidNotReceive()
            .UnsubscribeAllAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
