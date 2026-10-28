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

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Domain.Identity.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves a data request or consent change reaches the channel's dashboard clients live: one
/// <c>DataRightsChanged</c> push per event, naming only the change, so the "Your data" page reloads with no polling.
/// </summary>
public sealed class DataRightsBroadcastHandlerTests
{
    private static readonly Guid Subject = Guid.CreateVersion7();

    [Fact]
    public async Task HandleAsync_ErasureRequested_PushesRequestedToThatChannel()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        DataRightsBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new SubjectErasureRequestedEvent
            {
                BroadcasterId = channel,
                ErasureRequestId = Guid.CreateVersion7(),
                SubjectUserId = Subject,
                SubjectIdHash = "hash",
                RequestType = "erasure",
                RequestedBy = "self_service",
                Scope = "channel",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "DataRightsChanged",
                Arg.Is<DataRightsChangedAlertDto>(d => d.Change == "requested"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_ConsentChanged_PushesConsentToThatChannel()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        DataRightsBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new ConsentChangedEvent
            {
                BroadcasterId = channel,
                ConsentRecordId = Guid.CreateVersion7(),
                SubjectUserId = Subject,
                SubjectIdHash = "hash",
                ConsentType = "marketing",
                Status = "withdrawn",
                LawfulBasis = "consent",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "DataRightsChanged",
                Arg.Is<DataRightsChangedAlertDto>(d => d.Change == "consent"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_APlatformLevelEvent_PushesNothing()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        DataRightsBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new SubjectErasureFailedEvent
            {
                BroadcasterId = Guid.Empty,
                ErasureRequestId = Guid.CreateVersion7(),
                SubjectUserId = Subject,
                SubjectIdHash = "hash",
                FailureReason = "x",
            }
        );

        await notifier.DidNotReceive().NotifyChannelAsync(default!, default!, default!);
    }
}
