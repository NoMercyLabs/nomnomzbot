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
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Domain.CustomEvents.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.CustomEvents;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomEvents;

/// <summary>
/// Proves every create, edit and delete of a custom data source publishes one
/// <see cref="CustomDataSourceChangedEvent"/> for its channel, so the custom events page stays live.
/// </summary>
public sealed class CustomDataSourceChangedPublishTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static CustomDataSourceService Build(
        CustomDataSourceServiceTestDbContext db,
        IEventBus bus
    ) =>
        new(
            db,
            Substitute.For<ITokenProtector>(),
            Substitute.For<ICustomDataIngestService>(),
            Substitute.For<ICustomDataEgressFetcher>(),
            [],
            Substitute.For<IInboundWebhookEndpointService>(),
            bus
        );

    private static UpsertCustomDataSourceRequest Request(string displayName) =>
        new(
            Name: "sensor",
            DisplayName: displayName,
            SourceKind: "poll",
            PresetKey: null,
            EndpointUrl: null,
            AuthSecret: null,
            FieldMap: new Dictionary<string, string> { ["bpm"] = "$.data.heartRate" },
            PollIntervalSeconds: 30,
            IsEnabled: true
        );

    private static bool Matches(CustomDataSourceChangedEvent e, string change) =>
        e.BroadcasterId == Broadcaster && e.SourceName == "sensor" && e.Change == change;

    [Fact]
    public async Task CreateAsync_PublishesOneCreatedEventForTheChannel()
    {
        IEventBus bus = Substitute.For<IEventBus>();
        CustomDataSourceService service = Build(CustomDataSourceServiceTestDbContext.New(), bus);

        Result<CustomDataSourceDto> created = await service.CreateAsync(
            Broadcaster,
            Guid.CreateVersion7(),
            Request("Sensor Feed")
        );

        created.IsSuccess.Should().BeTrue();
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<CustomDataSourceChangedEvent>(e => Matches(e, "created")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task UpdateAsync_PublishesOneUpdatedEvent()
    {
        IEventBus bus = Substitute.For<IEventBus>();
        CustomDataSourceService service = Build(CustomDataSourceServiceTestDbContext.New(), bus);
        Guid actor = Guid.CreateVersion7();
        Result<CustomDataSourceDto> created = await service.CreateAsync(
            Broadcaster,
            actor,
            Request("Sensor Feed")
        );

        Result<CustomDataSourceDto> updated = await service.UpdateAsync(
            Broadcaster,
            created.Value.Id,
            actor,
            Request("Heart Rate")
        );

        updated.IsSuccess.Should().BeTrue();
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<CustomDataSourceChangedEvent>(e => Matches(e, "updated")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task DeleteAsync_PublishesOneDeletedEvent()
    {
        IEventBus bus = Substitute.For<IEventBus>();
        CustomDataSourceService service = Build(CustomDataSourceServiceTestDbContext.New(), bus);
        Guid actor = Guid.CreateVersion7();
        Result<CustomDataSourceDto> created = await service.CreateAsync(
            Broadcaster,
            actor,
            Request("Sensor Feed")
        );

        Result deleted = await service.DeleteAsync(Broadcaster, created.Value.Id, actor);

        deleted.IsSuccess.Should().BeTrue();
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<CustomDataSourceChangedEvent>(e => Matches(e, "deleted")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task DeleteAsync_AnUnknownSource_PublishesNothing()
    {
        IEventBus bus = Substitute.For<IEventBus>();
        CustomDataSourceService service = Build(CustomDataSourceServiceTestDbContext.New(), bus);

        Result deleted = await service.DeleteAsync(
            Broadcaster,
            Guid.CreateVersion7(),
            Guid.CreateVersion7()
        );

        deleted.IsFailure.Should().BeTrue();
        await bus.DidNotReceive()
            .PublishAsync(Arg.Any<CustomDataSourceChangedEvent>(), Arg.Any<CancellationToken>());
    }
}
