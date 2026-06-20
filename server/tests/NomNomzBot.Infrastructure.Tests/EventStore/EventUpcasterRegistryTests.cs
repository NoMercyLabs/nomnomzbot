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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Infrastructure.EventStore;

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// Behavior tests for the upcaster registry: the current version it reports, the transform it applies on read,
/// and the no-op pass-through for already-current payloads. Asserts the transformed payload content, not just
/// success.
/// </summary>
public sealed class EventUpcasterRegistryTests
{
    [Fact]
    public void CurrentVersion_IsOnePastHighestRegisteredFromVersion()
    {
        EventUpcasterRegistry registry = new([new CounterV1ToV2Upcaster()]);

        registry.CurrentVersion("counter.incremented").Should().Be(2);
        registry
            .CurrentVersion("unknown.event")
            .Should()
            .Be(1, "an event type with no upcaster is implicitly at version 1");
    }

    [Fact]
    public void UpcastToCurrent_TransformsV1PayloadToV2Shape()
    {
        EventUpcasterRegistry registry = new([new CounterV1ToV2Upcaster()]);

        Result<UpcastResult> result = registry.UpcastToCurrent(
            "counter.incremented",
            fromVersion: 1,
            payloadJson: "{\"key\":\"hits\",\"value\":42}"
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Changed.Should().BeTrue();
        result.Value.ToVersion.Should().Be(2);
        result.Value.PayloadJson.Should().Contain("\"amount\":42");
        result.Value.PayloadJson.Should().NotContain("\"value\"", "the v1 field was renamed away");
    }

    [Fact]
    public void UpcastToCurrent_AlreadyCurrent_IsNoOpPassThrough()
    {
        EventUpcasterRegistry registry = new([new CounterV1ToV2Upcaster()]);
        const string payload = "{\"key\":\"hits\",\"amount\":7}";

        Result<UpcastResult> result = registry.UpcastToCurrent("counter.incremented", 2, payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.Changed.Should().BeFalse();
        result
            .Value.PayloadJson.Should()
            .Be(payload, "a current-version payload is returned unchanged");
    }

    [Fact]
    public void Constructor_DuplicateStep_Throws()
    {
        Action act = () =>
            _ = new EventUpcasterRegistry([
                new CounterV1ToV2Upcaster(),
                new CounterV1ToV2Upcaster(),
            ]);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*Duplicate upcaster*",
                "each (EventType, FromVersion) step must be unique"
            );
    }
}
