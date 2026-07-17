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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Domain.Webhooks.Events;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks.EventHandlers;
using NSubstitute;
using Xunit;

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
        IPipelineEngine Pipeline,
        IEventResponseExecutor Responses
    ) Build(string payloadJson)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IEventJournal journal = Substitute.For<IEventJournal>();
        journal
            .GetByEventIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Record(payloadJson)));
        IPipelineEngine pipeline = Substitute.For<IPipelineEngine>();
        pipeline
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new PipelineExecutionResult
                {
                    ExecutionId = "x",
                    Outcome = PipelineOutcome.Completed,
                    Duration = TimeSpan.Zero,
                }
            );
        IEventResponseExecutor responses = Substitute.For<IEventResponseExecutor>();
        InboundWebhookAutomationBridge sut = new(
            db,
            journal,
            pipeline,
            responses,
            NullLogger<InboundWebhookAutomationBridge>.Instance
        );
        return (sut, db, pipeline, responses);
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
        string? targetEventType
    )
    {
        db.InboundWebhookEndpoints.Add(
            new InboundWebhookEndpoint
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
            }
        );
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
    public async Task Pipeline_target_runs_the_bound_pipeline_with_namespaced_variables()
    {
        (
            InboundWebhookAutomationBridge sut,
            AuthDbContext db,
            IPipelineEngine pipeline,
            IEventResponseExecutor responses
        ) = Build("{\"amount\":\"5\",\"from\":\"bob\"}");
        await SeedEndpointAsync(db, TargetPipeline, targetEventType: null);

        await sut.HandleAsync(Received());

        PipelineRequest req = (PipelineRequest)pipeline.ReceivedCalls().Single().GetArguments()[0]!;
        req.PipelineId.Should().Be(TargetPipeline);
        req.BroadcasterId.Should().Be(Channel);
        req.InitialVariables.Should().Contain("payload.amount", "5");
        req.InitialVariables.Should().Contain("payload.from", "bob");
        req.InitialVariables.Should().Contain("webhook.event_type", "webhook.github.push");
        req.InitialVariables.Should().Contain("webhook.provider", "github");
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
    public async Task Event_type_target_triggers_the_event_response_with_the_payload()
    {
        (
            InboundWebhookAutomationBridge sut,
            AuthDbContext db,
            IPipelineEngine pipeline,
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
        await pipeline
            .DidNotReceive()
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_endpoint_with_no_target_drives_nothing()
    {
        (
            InboundWebhookAutomationBridge sut,
            AuthDbContext db,
            IPipelineEngine pipeline,
            IEventResponseExecutor responses
        ) = Build("{\"amount\":\"5\"}");
        await SeedEndpointAsync(db, targetPipelineId: null, targetEventType: null);

        await sut.HandleAsync(Received());

        await pipeline
            .DidNotReceive()
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>());
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
        (InboundWebhookAutomationBridge sut, AuthDbContext db, IPipelineEngine pipeline, _) = Build(
            "{\"amount\":\"5\"}"
        );
        await SeedEndpointAsync(db, TargetPipeline, targetEventType: null);

        await sut.HandleAsync(Received(wasDuplicate: true));

        await pipeline
            .DidNotReceive()
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>());
    }
}
