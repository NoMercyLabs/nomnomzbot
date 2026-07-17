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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.CustomEvents.Events;
using NomNomzBot.Infrastructure.CustomEvents.EventHandlers;
using NSubstitute;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.CustomEvents;

/// <summary>
/// Proves the custom-events.md D1(a) seam: an ingested datum fires a <c>custom.&lt;name&gt;</c>
/// event-response trigger, seeding the extracted fields as <c>custom.&lt;name&gt;.&lt;field&gt;</c>
/// variables. A tenant-less (Guid.Empty) event drives nothing.
/// </summary>
public sealed class CustomDataTriggerHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");

    private static (CustomDataTriggerHandler Sut, IEventResponseExecutor Responses) Build()
    {
        IEventResponseExecutor responses = Substitute.For<IEventResponseExecutor>();
        CustomDataTriggerHandler sut = new(
            responses,
            NullLogger<CustomDataTriggerHandler>.Instance
        );
        return (sut, responses);
    }

    private static CustomDataReceivedEvent Received(
        Guid broadcasterId,
        string sourceName,
        IReadOnlyDictionary<string, string> fields
    ) =>
        new()
        {
            BroadcasterId = broadcasterId,
            SourceName = sourceName,
            Fields = fields,
            RawPayload = "{}",
        };

    [Fact]
    public async Task Ingested_datum_fires_the_custom_source_trigger_with_namespaced_fields()
    {
        (CustomDataTriggerHandler sut, IEventResponseExecutor responses) = Build();

        await sut.HandleAsync(
            Received(Channel, "heartrate", new Dictionary<string, string> { ["bpm"] = "128" })
        );

        await responses
            .Received(1)
            .ExecuteAsync(
                Channel,
                "custom.heartrate",
                null,
                "heartrate",
                Arg.Is<Dictionary<string, string>>(v =>
                    v["custom.heartrate.bpm"] == "128" && v["custom.source"] == "heartrate"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_tenant_less_event_drives_nothing()
    {
        (CustomDataTriggerHandler sut, IEventResponseExecutor responses) = Build();

        await sut.HandleAsync(
            Received(Guid.Empty, "heartrate", new Dictionary<string, string> { ["bpm"] = "128" })
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
}
