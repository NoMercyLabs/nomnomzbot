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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.CustomEvents.Events;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Domain.Webhooks.Events;
using NomNomzBot.Infrastructure.CustomEvents.EventHandlers;
using NomNomzBot.Infrastructure.Obs.EventHandlers;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// The OBS, custom-data and inbound-webhook triggers each offer a sample to the script test run, and the
/// sample carries exactly the variable keys the live trigger sets.
/// </summary>
public sealed class AutomationTriggerSampleTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e01");
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] LaneNamespaces =
    [
        "NomNomzBot.Infrastructure.Obs",
        "NomNomzBot.Infrastructure.CustomEvents",
        "NomNomzBot.Infrastructure.Webhooks",
    ];

    // Only this lane's sources: they take no dependencies, so the catalog builds without the whole container.
    private static IEnumerable<Type> LaneSourceTypes() =>
        typeof(TriggerSampleCatalog)
            .Assembly.GetTypes()
            .Where(type =>
                type is { IsAbstract: false, IsInterface: false }
                && typeof(ITriggerSampleSource).IsAssignableFrom(type)
                && LaneNamespaces.Any(ns =>
                    type.Namespace!.StartsWith(ns, StringComparison.Ordinal)
                )
            );

    private static ITriggerSampleCatalog Catalog() =>
        new TriggerSampleCatalog(
            [
                .. LaneSourceTypes()
                    .Select(type => (ITriggerSampleSource)Activator.CreateInstance(type)!),
            ],
            new FakeTimeProvider(Now)
        );

    [Fact]
    public void The_assembly_scan_registers_every_lane_source()
    {
        ServiceCollection services = new();
        services.AddImplementationsOf<ITriggerSampleSource>(
            typeof(TriggerSampleCatalog).Assembly,
            ServiceLifetime.Scoped
        );

        List<Type> registered =
        [
            .. services
                .Where(d => d.ServiceType == typeof(ITriggerSampleSource))
                .Select(d => d.ImplementationType!),
        ];

        List<Type> lane = [.. LaneSourceTypes()];
        lane.Should().HaveCount(5);
        registered.Should().Contain(lane);
    }

    private static TriggerSample Sample(string id)
    {
        TriggerSample? sample = Catalog().Find(id);
        sample.Should().NotBeNull($"the catalog must list a sample with id {id}");
        return sample;
    }

    [Theory]
    [InlineData(
        "obs.CurrentProgramSceneChanged",
        new[] { "obs.event.type", "obs.event.sceneName", "obs.event.sceneUuid" }
    )]
    [InlineData(
        "obs.StreamStateChanged",
        new[] { "obs.event.type", "obs.event.outputActive", "obs.event.outputState" }
    )]
    [InlineData(
        "obs.InputMuteStateChanged",
        new[]
        {
            "obs.event.type",
            "obs.event.inputName",
            "obs.event.inputUuid",
            "obs.event.inputMuted",
        }
    )]
    public void An_obs_event_sample_carries_the_keys_the_live_trigger_sets(
        string id,
        string[] expectedKeys
    )
    {
        TriggerSample sample = Sample(id);

        sample.ResponseKey.Should().Be(id);
        sample.UserId.Should().BeNull();
        sample.Variables["obs.event.type"].Should().Be(id["obs.".Length..]);
        sample.Variables.Keys.Should().BeEquivalentTo(expectedKeys);

        Dictionary<string, string> data = sample
            .Variables.Where(pair => pair.Key != "obs.event.type")
            .ToDictionary(
                pair => pair.Key["obs.event.".Length..],
                pair => pair.Value,
                StringComparer.Ordinal
            );
        string dataJson = System.Text.Json.JsonSerializer.Serialize(
            data.ToDictionary(
                pair => pair.Key,
                object (pair) => bool.TryParse(pair.Value, out bool flag) ? flag : pair.Value
            )
        );
        Dictionary<string, string> live = ObsEventTriggerSource.BuildVariables(
            new ObsEventReceivedEvent
            {
                BroadcasterId = Channel,
                ObsEventType = id["obs.".Length..],
                DataJson = dataJson,
            }
        );
        live.Should().BeEquivalentTo(sample.Variables);
    }

    [Fact]
    public async Task The_custom_data_sample_matches_what_the_live_handler_sets()
    {
        TriggerSample sample = Sample("custom.example");
        sample.ResponseKey.Should().Be("custom.example");
        sample.Variables["custom.source"].Should().Be("example");

        Dictionary<string, string> fields = sample
            .Variables.Where(pair =>
                pair.Key.StartsWith("custom.example.", StringComparison.Ordinal)
            )
            .ToDictionary(
                pair => pair.Key["custom.example.".Length..],
                pair => pair.Value,
                StringComparer.Ordinal
            );
        fields.Should().HaveCount(2);

        IEventResponseExecutor responses = Substitute.For<IEventResponseExecutor>();
        Dictionary<string, string>? live = null;
        await responses.ExecuteAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Do<Dictionary<string, string>>(v => live = v),
            Arg.Any<CancellationToken>()
        );
        CustomDataTriggerHandler handler = new(
            responses,
            NullLogger<CustomDataTriggerHandler>.Instance
        );

        await handler.HandleAsync(
            new CustomDataReceivedEvent
            {
                BroadcasterId = Channel,
                SourceName = "example",
                Fields = fields,
                RawPayload = "{}",
            }
        );

        live.Should().NotBeNull();
        live!.Should().BeEquivalentTo(sample.Variables);
    }

    [Fact]
    public async Task The_webhook_sample_matches_what_the_live_bridge_sets()
    {
        TriggerSample sample = Sample("webhook");
        sample.ResponseKey.Should().Be("webhook");
        sample.UserId.Should().Be("webhook");

        Dictionary<string, string> payload = sample
            .Variables.Where(pair => pair.Key.StartsWith("payload.", StringComparison.Ordinal))
            .ToDictionary(
                pair => pair.Key["payload.".Length..],
                pair => pair.Value,
                StringComparer.Ordinal
            );
        payload.Should().NotBeEmpty();

        Guid endpointId = Guid.Parse("0192a000-0000-7000-8000-000000000e02");
        Guid journalId = Guid.Parse("0192a000-0000-7000-8000-000000000e03");
        DateTime when = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.InboundWebhookEndpoints.Add(
            new()
            {
                Id = endpointId,
                BroadcasterId = Channel,
                Name = "gh",
                Token = "tok",
                AdapterKind = WebhookAdapterKind.Generic,
                VerificationSecretEnvelope = "sealed",
                EncryptionKeyId = Guid.Parse("0192a000-0000-7000-8000-000000000ebb"),
                IsEnabled = true,
                TargetEventType = "webhook.generic.event",
                CreatedAt = when,
                UpdatedAt = when,
            }
        );
        await db.SaveChangesAsync();
        IEventJournal journal = Substitute.For<IEventJournal>();
        journal
            .GetByEventIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new EventRecord(
                        1,
                        journalId,
                        Channel,
                        1,
                        sample.Variables["webhook.event_type"],
                        1,
                        "webhook",
                        System.Text.Json.JsonSerializer.Serialize(payload),
                        false,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        "{}",
                        when,
                        when
                    )
                )
            );
        IEventResponseExecutor responses = Substitute.For<IEventResponseExecutor>();
        Dictionary<string, string>? live = null;
        await responses.ExecuteAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Do<Dictionary<string, string>>(v => live = v),
            Arg.Any<CancellationToken>()
        );
        InboundWebhookAutomationBridge bridge = new(
            db,
            journal,
            Substitute.For<IPipelineEngine>(),
            responses,
            NullLogger<InboundWebhookAutomationBridge>.Instance
        );

        await bridge.HandleAsync(
            new InboundWebhookReceivedEvent
            {
                BroadcasterId = Channel,
                InboundEndpointId = endpointId,
                Adapter = Enum.Parse<WebhookAdapterKind>(
                    sample.Variables["webhook.provider"],
                    ignoreCase: true
                ),
                EventType = sample.Variables["webhook.event_type"],
                JournalEventId = journalId,
                StreamPosition = 1,
                ProviderEventId = sample.Variables["webhook.provider_event_id"],
                WasDuplicate = false,
            }
        );

        live.Should().NotBeNull();
        live!.Should().BeEquivalentTo(sample.Variables);
    }
}
