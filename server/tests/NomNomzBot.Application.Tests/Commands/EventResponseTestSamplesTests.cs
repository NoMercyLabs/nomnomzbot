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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using Xunit.Abstractions;

namespace NomNomzBot.Application.Tests.Commands;

/// <summary>
/// The sample event a test fire builds, for EVERY event type of the response table: each preset variable has a
/// non-empty value, the acting-user variables are the caller's own, and the rest come from the existing sample table.
/// </summary>
public sealed class EventResponseTestSamplesTests(ITestOutputHelper output)
{
    private static readonly EventResponseTestActor Caller = new(
        "4242",
        "stoney_login",
        "Stoney_Eagle"
    );

    public static TheoryData<string> EveryEventType()
    {
        TheoryData<string> data = [];
        foreach (string eventType in EventResponsePresetCatalog.EventTypes)
            data.Add(eventType);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryEventType))]
    public void Each_event_type_builds_a_sample_that_covers_every_variable_its_preset_declares(
        string eventType
    )
    {
        EventResponsePresetDto preset = EventResponsePresetCatalog.Presets.Single(p =>
            p.EventType == eventType
        );

        Dictionary<string, string>? sample = EventResponseTestSamples.For(eventType, Caller);

        sample.Should().NotBeNull();
        foreach (string variable in preset.Variables)
            sample!
                .Should()
                .ContainKey(variable)
                .WhoseValue.Should()
                .NotBeNullOrWhiteSpace($"{eventType} declares {{{variable}}}");
    }

    [Fact]
    public void Every_event_type_of_the_response_table_is_covered_and_the_count_is_reported()
    {
        List<string> uncovered = [];
        foreach (string eventType in EventResponsePresetCatalog.EventTypes)
        {
            Dictionary<string, string>? sample = EventResponseTestSamples.For(eventType, Caller);
            IReadOnlyList<string> declared = EventResponsePresetCatalog
                .Presets.Single(p => p.EventType == eventType)
                .Variables;
            if (
                sample is null
                || declared.Any(v => string.IsNullOrWhiteSpace(sample.GetValueOrDefault(v)))
            )
                uncovered.Add(eventType);
        }

        int total = EventResponsePresetCatalog.EventTypes.Count;
        output.WriteLine($"{total - uncovered.Count} of {total} event types covered");
        uncovered.Should().BeEmpty();
    }

    [Fact]
    public void A_raid_sample_names_the_caller_as_the_raider_with_25_viewers()
    {
        Dictionary<string, string> sample = EventResponseTestSamples.For("channel.raid", Caller)!;

        sample["user"].Should().Be("Stoney_Eagle");
        sample["user.name"].Should().Be("stoney_login");
        sample["user.id"].Should().Be("4242");
        sample["viewers"].Should().Be("25");
    }

    [Fact]
    public void An_event_type_outside_the_response_table_has_no_sample()
    {
        EventResponseTestSamples.For("channel.not_a_real_event", Caller).Should().BeNull();
    }
}
