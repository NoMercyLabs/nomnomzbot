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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Domain.Webhooks.Events;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks.EventHandlers;
using NSubstitute;
using NSubstitute.Core;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Proves the inbound→automation seam (webhooks.md §3.2 step 5-6): a verified inbound webhook runs the endpoint's
/// configured target — a bound pipeline (by TargetPipelineId) OR an event-response (by TargetEventType) — with the
/// journaled payload reconstructed as payload.* variables plus webhook.* source metadata. An endpoint with no
/// target, and a duplicate delivery, drive nothing (so a supporter-only endpoint never double-fires).
/// </summary>
public sealed class InboundWebhookAutomationBridgeTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000c01");
    private static readonly Guid Endpoint = Guid.Parse("0192a000-0000-7000-8000-000000000c02");
    private static readonly Guid JournalEventId = Guid.Parse(
        "0192a000-0000-7000-8000-000000000c03"
    );
    private static readonly Guid TargetPipeline = Guid.Parse(
        "0192a000-0000-7000-8000-000000000c04"
    );
    private static readonly DateTime When = new(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc);

    private static (
        InboundWebhookAutomationBridge Sut,
        AuthDbContext Db,
        ICommandInvoker Invoker,
        IEventResponseExecutor Responses
    ) Build(string payloadJson)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IEventJournal journal = Substitute.For<IEventJournal>();
        journal
            .GetByEventIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Record(payloadJson)));
        ICommandInvoker invoker = Substitute.For<ICommandInvoker>();
        invoker
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new InvocationOutcome(true, null, null)));
        IEventResponseExecutor responses = Substitute.For<IEventResponseExecutor>();
        InboundWebhookAutomationBridge sut = new(
            db,
            journal,
            invoker,
            responses,
            NullLogger<InboundWebhookAutomationBridge>.Instance
        );
        return (sut, db, invoker, responses);
    }

    private static (BindingTarget Target, InvocationContext Context, Guid Channel) OnlyInvocation(
        ICommandInvoker invoker
    )
    {
        ICall call = invoker.ReceivedCalls().Single();
        object?[] args = call.GetArguments();
        return ((BindingTarget)args[1]!, (InvocationContext)args[2]!, (Guid)args[0]!);
    }

    private static EventRecord Record(string payloadJson) =>
        new(
            1,
            JournalEventId,
            Channel,
            42,
            "webhook.github.push",
            1,
            "webhook",
            payloadJson,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            "{}",
            When,
            When
        );

    private static async Task SeedEndpointAsync(
        AuthDbContext db,
        Guid? targetPipelineId,
        string? targetEventType,
        BindingTarget? target = null
    )
    {
        InboundWebhookEndpoint endpoint = new()
        {
            Id = Endpoint,
            BroadcasterId = Channel,
            Name = "gh",
            Token = "tok",
            AdapterKind = WebhookAdapterKind.Github,
            VerificationSecretEnvelope = "sealed",
            EncryptionKeyId = Guid.Parse("0192a000-0000-7000-8000-000000000cbb"),
            IsEnabled = true,
            TargetPipelineId = targetPipelineId,
            TargetEventType = targetEventType,
            CreatedAt = When,
            UpdatedAt = When,
        };
        if (target is not null)
            endpoint.SetBindingTarget(target);
        db.InboundWebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync();
    }

    private static InboundWebhookReceivedEvent Received(bool wasDuplicate = false) =>
        new()
        {
            BroadcasterId = Channel,
            InboundEndpointId = Endpoint,
            Adapter = WebhookAdapterKind.Github,
            EventType = "webhook.github.push",
            JournalEventId = JournalEventId,
            StreamPosition = 42,
            ProviderEventId = "del_1",
            WasDuplicate = wasDuplicate,
        };

    [Fact]
    public async Task A_pipeline_only_webhook_still_runs_its_pipeline_with_namespaced_variables()
    {
        (
            InboundWebhookAutomationBridge sut,
            AuthDbContext db,
            ICommandInvoker invoker,
            IEventResponseExecutor responses
        ) = Build("{\"amount\":\"5\",\"from\":\"bob\"}");
        await SeedEndpointAsync(db, TargetPipeline, targetEventType: null);

        await sut.HandleAsync(Received());

        (BindingTarget target, InvocationContext context, Guid channel) = OnlyInvocation(invoker);
        target.Kind.Should().Be(BindingTargetKind.Pipeline);
        target.Id.Should().Be(TargetPipeline.ToString());
        channel.Should().Be(Channel);
        context.Variables.Should().Contain("payload.amount", "5");
        context.Variables.Should().Contain("payload.from", "bob");
        context.Variables.Should().Contain("webhook.event_type", "webhook.github.push");
        context.Variables.Should().Contain("webhook.provider", "github");
        await responses
            .DidNotReceive()
            .ExecuteAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_action_target_runs_that_action_with_the_payload_variables()
    {
        (InboundWebhookAutomationBridge sut, AuthDbContext db, ICommandInvoker invoker, _) = Build(
            "{\"amount\":\"5\",\"from\":\"bob\"}"
        );
        await SeedEndpointAsync(
            db,
            targetPipelineId: null,
            targetEventType: null,
            new BindingTarget(BindingTargetKind.Action, ActionType: "chat_message")
        );

        await sut.HandleAsync(Received());

        (BindingTarget target, InvocationContext context, Guid channel) = OnlyInvocation(invoker);
        target.Kind.Should().Be(BindingTargetKind.Action);
        target.ActionType.Should().Be("chat_message");
        channel.Should().Be(Channel);
        context.TriggerSource.Should().Be("webhook");
        context.Variables.Should().Contain("payload.amount", "5");
        context.Variables.Should().Contain("payload.from", "bob");
        context.Variables.Should().Contain("webhook.provider", "github");
    }

    [Fact]
    public async Task A_command_target_runs_that_command_with_the_payload_variables()
    {
        (InboundWebhookAutomationBridge sut, AuthDbContext db, ICommandInvoker invoker, _) = Build(
            "{\"stars\":\"42\"}"
        );
        Guid commandId = Guid.Parse("0192a000-0000-7000-8000-000000000c05");
        await SeedEndpointAsync(
            db,
            targetPipelineId: null,
            targetEventType: null,
            new BindingTarget(BindingTargetKind.Command, commandId.ToString())
        );

        await sut.HandleAsync(Received());

        (BindingTarget target, InvocationContext context, _) = OnlyInvocation(invoker);
        target.Kind.Should().Be(BindingTargetKind.Command);
        target.Id.Should().Be(commandId.ToString());
        context.Variables.Should().Contain("payload.stars", "42");
    }

    [Fact]
    public async Task A_target_that_fails_to_run_never_throws_into_the_event_bus()
    {
        (InboundWebhookAutomationBridge sut, AuthDbContext db, ICommandInvoker invoker, _) = Build(
            "{\"stars\":\"42\"}"
        );
        invoker
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Result<InvocationOutcome>>(_ => throw new InvalidOperationException("boom"));
        await SeedEndpointAsync(db, TargetPipeline, targetEventType: null);

        Func<Task> act = () => sut.HandleAsync(Received());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Event_type_target_triggers_the_event_response_with_the_payload()
    {
        (
            InboundWebhookAutomationBridge sut,
            AuthDbContext db,
            ICommandInvoker invoker,
            IEventResponseExecutor responses
        ) = Build("{\"stars\":\"42\"}");
        await SeedEndpointAsync(db, targetPipelineId: null, targetEventType: "custom.github.star");

        await sut.HandleAsync(Received());

        await responses
            .Received(1)
            .ExecuteAsync(
                Channel,
                "custom.github.star",
                null,
                "gh",
                Arg.Is<Dictionary<string, string>>(v => v["payload.stars"] == "42"),
                Arg.Any<CancellationToken>()
            );
        await invoker
            .DidNotReceive()
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_endpoint_with_no_target_drives_nothing()
    {
        (
            InboundWebhookAutomationBridge sut,
            AuthDbContext db,
            ICommandInvoker invoker,
            IEventResponseExecutor responses
        ) = Build("{\"amount\":\"5\"}");
        await SeedEndpointAsync(db, targetPipelineId: null, targetEventType: null);

        await sut.HandleAsync(Received());

        await invoker
            .DidNotReceive()
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            );
        await responses
            .DidNotReceive()
            .ExecuteAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_duplicate_delivery_is_ignored()
    {
        (InboundWebhookAutomationBridge sut, AuthDbContext db, ICommandInvoker invoker, _) = Build(
            "{\"amount\":\"5\"}"
        );
        await SeedEndpointAsync(db, TargetPipeline, targetEventType: null);

        await sut.HandleAsync(Received(wasDuplicate: true));

        await invoker
            .DidNotReceive()
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            );
    }
}
