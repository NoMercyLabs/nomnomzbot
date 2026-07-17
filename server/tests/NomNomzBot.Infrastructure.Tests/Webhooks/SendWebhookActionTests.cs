// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Infrastructure.Webhooks.PipelineActions;
using NSubstitute;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Proves the <c>send_webhook</c> pipeline action (webhooks.md §6): it hands the current pipeline variables to the
/// named outbound endpoint via the dispatcher (default event type <c>pipeline.send_webhook</c>, overridable), and
/// fails loudly — without enqueuing — on a missing/invalid endpoint id or a dispatcher rejection.
/// </summary>
public sealed class SendWebhookActionTests
{
    private static readonly Guid Channel = Guid.Parse("019f2a00-2222-7000-8000-000000000001");
    private static readonly Guid EndpointId = Guid.Parse("019f2a00-2222-7000-8000-000000000002");

    private static PipelineExecutionContext Context()
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "viewer-9",
            TriggeredByDisplayName = "viewer",
            MessageId = "m1",
            RawMessage = "",
            CancellationToken = default,
        };
        ctx.Variables["payload.amount"] = "5";
        return ctx;
    }

    private static ActionDefinition Action(params (string Key, object Value)[] p) =>
        new()
        {
            Type = "send_webhook",
            Parameters = p.ToDictionary(
                x => x.Key,
                x => JsonSerializer.SerializeToElement(x.Value)
            ),
        };

    private static (SendWebhookAction Action, IOutboundWebhookDispatcher Dispatcher) Build(
        Result<OutboundEnqueueResult>? enqueueResult = null
    )
    {
        IOutboundWebhookDispatcher dispatcher = Substitute.For<IOutboundWebhookDispatcher>();
        dispatcher
            .EnqueueForEndpointAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                enqueueResult
                    ?? Result.Success(
                        new OutboundEnqueueResult(
                            EndpointId,
                            Guid.Empty,
                            1,
                            WebhookDeliveryStatus.Delivered
                        )
                    )
            );
        return (new SendWebhookAction(dispatcher), dispatcher);
    }

    [Fact]
    public async Task Enqueues_to_the_named_endpoint_with_the_pipeline_variables()
    {
        (SendWebhookAction action, IOutboundWebhookDispatcher dispatcher) = Build();

        ActionResult result = await action.ExecuteAsync(
            Context(),
            Action(("endpoint", EndpointId.ToString()))
        );

        result.Succeeded.Should().BeTrue();
        await dispatcher
            .Received(1)
            .EnqueueForEndpointAsync(
                Channel,
                EndpointId,
                "pipeline.send_webhook",
                Arg.Is<IReadOnlyDictionary<string, string>>(v => v["payload.amount"] == "5"),
                null,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Passes_through_a_custom_event_type()
    {
        (SendWebhookAction action, IOutboundWebhookDispatcher dispatcher) = Build();

        await action.ExecuteAsync(
            Context(),
            Action(("endpoint", EndpointId.ToString()), ("event_type", "order.created"))
        );

        await dispatcher
            .Received(1)
            .EnqueueForEndpointAsync(
                Channel,
                EndpointId,
                "order.created",
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                null,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Missing_endpoint_fails_without_enqueuing()
    {
        (SendWebhookAction action, IOutboundWebhookDispatcher dispatcher) = Build();

        ActionResult result = await action.ExecuteAsync(Context(), Action());

        result.Succeeded.Should().BeFalse();
        await dispatcher
            .DidNotReceive()
            .EnqueueForEndpointAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_unparseable_endpoint_id_fails_without_enqueuing()
    {
        (SendWebhookAction action, IOutboundWebhookDispatcher dispatcher) = Build();

        ActionResult result = await action.ExecuteAsync(
            Context(),
            Action(("endpoint", "not-a-guid"))
        );

        result.Succeeded.Should().BeFalse();
        await dispatcher
            .DidNotReceive()
            .EnqueueForEndpointAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_dispatcher_rejection_surfaces_the_reason()
    {
        (SendWebhookAction action, _) = Build(
            Result.Failure<OutboundEnqueueResult>(
                "That outbound endpoint is disabled.",
                "FEATURE_DISABLED"
            )
        );

        ActionResult result = await action.ExecuteAsync(
            Context(),
            Action(("endpoint", EndpointId.ToString()))
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("That outbound endpoint is disabled.");
    }
}
