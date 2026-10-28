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
using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Webhooks.Enums;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the AdminHub is no longer a dead pipe: channel online/offline flips and tenant suspensions each
/// push the operator-facing message with the load-bearing payload fields.
/// </summary>
public sealed class AdminBroadcastHandlersTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000dd01");

    private static (IHubContext<AdminHub, IAdminClient> Hub, IAdminClient All) HubWithRecorder()
    {
        IHubContext<AdminHub, IAdminClient> hub = Substitute.For<
            IHubContext<AdminHub, IAdminClient>
        >();
        IAdminClient all = Substitute.For<IAdminClient>();
        hub.Clients.All.Returns(all);
        return (hub, all);
    }

    [Fact]
    public async Task Channel_online_pushes_a_live_registry_update()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? payload = null;
        all.ReceiveChannelRegistryUpdate(Arg.Do<object>(p => payload = p))
            .Returns(Task.CompletedTask);

        await new AdminChannelOnlineBroadcastHandler(hub).HandleAsync(
            new()
            {
                Provider = AuthEnums.Platform.Twitch,
                BroadcasterId = Channel,
                BroadcasterDisplayName = "Stoney",
                StreamTitle = "blame the lag",
                GameName = "Deep Rock",
                StartedAt = DateTimeOffset.UtcNow,
            }
        );

        payload.Should().NotBeNull();
        Type t = payload!.GetType();
        t.GetProperty("BroadcasterId")!.GetValue(payload).Should().Be(Channel);
        t.GetProperty("IsLive")!.GetValue(payload).Should().Be(true);
        t.GetProperty("ChannelName")!.GetValue(payload).Should().Be("Stoney");
    }

    [Fact]
    public async Task Trust_safety_change_pushes_area_action_and_subject_to_every_open_desk()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? payload = null;
        all.ReceiveTrustSafetyChanged(Arg.Do<object>(p => payload = p)).Returns(Task.CompletedTask);

        await new AdminTrustSafetyBroadcastHandler(hub).HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                Area = "network-block",
                Action = "lifted",
                SubjectId = Channel,
            }
        );

        payload.Should().NotBeNull();
        Type t = payload!.GetType();
        t.GetProperty("Area")!.GetValue(payload).Should().Be("network-block");
        t.GetProperty("Action")!.GetValue(payload).Should().Be("lifted");
        t.GetProperty("SubjectId")!.GetValue(payload).Should().Be(Channel);
    }

    [Theory]
    [InlineData(WebhookDeliveryStatus.Delivered)]
    [InlineData(WebhookDeliveryStatus.Failed)]
    [InlineData(WebhookDeliveryStatus.DeadLetter)]
    public async Task Resolved_webhook_attempt_tells_every_open_desk_which_tenant_budget_moved(
        WebhookDeliveryStatus status
    )
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? payload = null;
        all.ReceiveErrorBudgetChanged(Arg.Do<object>(p => payload = p)).Returns(Task.CompletedTask);

        await new AdminErrorBudgetBroadcastHandler(hub).HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                OutboundEndpointId = Guid.NewGuid(),
                WebhookMessageId = Guid.NewGuid(),
                Attempt = 1,
                Status = status,
            }
        );

        payload.Should().NotBeNull();
        Type t = payload!.GetType();
        t.GetProperty("BroadcasterId")!.GetValue(payload).Should().Be(Channel);
        t.GetProperty("Status")!.GetValue(payload).Should().Be(status.ToString());
    }

    [Fact]
    public async Task Pending_webhook_attempt_does_not_move_a_budget_so_nothing_is_pushed()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();

        await new AdminErrorBudgetBroadcastHandler(hub).HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                OutboundEndpointId = Guid.NewGuid(),
                WebhookMessageId = Guid.NewGuid(),
                Attempt = 1,
                Status = WebhookDeliveryStatus.Pending,
            }
        );

        await all.DidNotReceive().ReceiveErrorBudgetChanged(Arg.Any<object>());
    }

    [Fact]
    public async Task Content_change_pushes_action_and_definition_to_every_open_page()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? payload = null;
        all.ReceiveContentChanged(Arg.Do<object>(p => payload = p)).Returns(Task.CompletedTask);

        await new AdminContentBroadcastHandler(hub).HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                Action = "retired",
                DefinitionId = Channel,
            }
        );

        payload.Should().NotBeNull();
        Type t = payload!.GetType();
        t.GetProperty("Action")!.GetValue(payload).Should().Be("retired");
        t.GetProperty("DefinitionId")!.GetValue(payload).Should().Be(Channel);
    }

    [Fact]
    public async Task Channel_offline_pushes_a_not_live_registry_update()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? payload = null;
        all.ReceiveChannelRegistryUpdate(Arg.Do<object>(p => payload = p))
            .Returns(Task.CompletedTask);

        await new AdminChannelOfflineBroadcastHandler(hub).HandleAsync(
            new()
            {
                Provider = AuthEnums.Platform.Twitch,
                BroadcasterId = Channel,
                BroadcasterDisplayName = "Stoney",
                StreamDuration = TimeSpan.FromHours(3),
            }
        );

        payload!.GetType().GetProperty("IsLive")!.GetValue(payload).Should().Be(false);
    }

    [Fact]
    public async Task Tenant_suspension_pushes_a_registry_update_and_a_warning_log_line()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? log = null;
        all.ReceiveLog(Arg.Do<object>(p => log = p)).Returns(Task.CompletedTask);

        await new AdminTenantSuspensionBroadcastHandler(hub).HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                PrincipalId = Guid.NewGuid(),
                TargetBroadcasterId = Channel,
                NewStatus = "suspended",
                Reason = "ToS",
            }
        );

        await all.Received(1).ReceiveChannelRegistryUpdate(Arg.Any<object>());
        log.Should().NotBeNull();
        log!.GetType().GetProperty("Type")!.GetValue(log).Should().Be("warning");
        log.GetType()
            .GetProperty("Message")!
            .GetValue(log)!
            .ToString()
            .Should()
            .Contain("suspended");
    }
}
