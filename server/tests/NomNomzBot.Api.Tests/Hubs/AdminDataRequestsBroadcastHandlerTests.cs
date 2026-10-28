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
using NomNomzBot.Domain.Identity.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>Proves a data request change reaches every open operator data requests page with its change name and request id, and no personal data.</summary>
public sealed class AdminDataRequestsBroadcastHandlerTests
{
    private static readonly Guid Request = Guid.Parse("0192a000-0000-7000-8000-00000000ef01");
    private static readonly Guid Subject = Guid.Parse("0192a000-0000-7000-8000-00000000ef02");

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
    public async Task Failed_erasure_pushes_the_change_and_request_id_but_not_the_subject()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? payload = null;
        all.ReceiveDataRequestsChanged(Arg.Do<object>(p => payload = p))
            .Returns(Task.CompletedTask);

        await new AdminDataRequestsBroadcastHandler(hub).HandleAsync(
            new SubjectErasureFailedEvent
            {
                BroadcasterId = Guid.Empty,
                ErasureRequestId = Request,
                SubjectUserId = Subject,
                SubjectIdHash = "hash",
                FailureReason = "db",
            }
        );

        payload.Should().NotBeNull();
        Type t = payload!.GetType();
        t.GetProperty("Change")!.GetValue(payload).Should().Be("failed");
        t.GetProperty("ErasureRequestId")!.GetValue(payload).Should().Be(Request);
        t.GetProperties().Select(p => p.Name).Should().NotContain("SubjectUserId");
    }

    [Fact]
    public async Task Requested_completed_and_exported_each_push_their_own_change_name()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        List<object> payloads = [];
        all.ReceiveDataRequestsChanged(Arg.Do<object>(p => payloads.Add(p)))
            .Returns(Task.CompletedTask);
        AdminDataRequestsBroadcastHandler handler = new(hub);

        await handler.HandleAsync(
            new SubjectErasureRequestedEvent
            {
                BroadcasterId = Guid.Empty,
                ErasureRequestId = Request,
                SubjectUserId = Subject,
                SubjectIdHash = "hash",
                RequestType = "erasure",
                RequestedBy = "platform_iam",
                Scope = "deployment",
            }
        );
        await handler.HandleAsync(
            new SubjectErasureCompletedEvent
            {
                BroadcasterId = Guid.Empty,
                ErasureRequestId = Request,
                SubjectUserId = Subject,
                SubjectIdHash = "hash",
                CryptoShredApplied = true,
                AnonymizationApplied = true,
                KeysShredded = 1,
                RowsAffected = 3,
            }
        );
        await handler.HandleAsync(
            new SubjectDataExportedEvent
            {
                BroadcasterId = Guid.Empty,
                ErasureRequestId = Request,
                SubjectUserId = Subject,
                SubjectIdHash = "hash",
                ExportFormat = "json",
                ExportLocation = "/api/v1/gdpr/export",
                RowsAffected = 3,
            }
        );

        List<object?> changes = payloads
            .Select(p => p.GetType().GetProperty("Change")!.GetValue(p))
            .ToList();
        changes.Should().Equal("requested", "completed", "exported");
    }
}
