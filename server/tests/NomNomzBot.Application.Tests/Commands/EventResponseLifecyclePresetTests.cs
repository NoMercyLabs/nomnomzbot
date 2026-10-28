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

namespace NomNomzBot.Application.Tests.Commands;

/// <summary>
/// The poll start, the three prediction moments and the hype train start and end reach the event response
/// executor, so each has its own page row. The placeholders of a row are exactly the keys the handler's
/// <c>BuildVariables</c> passes (PollEventHandler, PredictionEventHandler, HypeTrainEventHandler).
/// </summary>
public sealed class EventResponseLifecyclePresetTests
{
    public static TheoryData<string, string[]> Rows() =>
        new()
        {
            { "channel.poll.begin", ["poll.id", "poll.title", "poll.duration", "poll.choices"] },
            {
                "channel.prediction.begin",
                ["prediction.id", "prediction.title", "prediction.window", "prediction.outcomes"]
            },
            {
                "channel.prediction.lock",
                ["prediction.id", "prediction.title", "prediction.total_points"]
            },
            {
                "channel.prediction.end",
                [
                    "prediction.id",
                    "prediction.title",
                    "prediction.status",
                    "prediction.winner",
                    "prediction.winner.points",
                    "prediction.winner.users",
                ]
            },
            {
                "channel.hype_train.begin",
                ["hype_train.id", "hype_train.level", "hype_train.total", "hype_train.goal"]
            },
            { "channel.hype_train.end", ["hype_train.id", "hype_train.level", "hype_train.total"] },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_event_has_a_row_with_the_placeholders_its_handler_passes(
        string eventType,
        string[] placeholders
    )
    {
        EventResponsePresetCatalog.EventTypes.Should().Contain(eventType);

        EventResponsePresetDto preset = EventResponsePresetCatalog.Presets.Single(p =>
            p.EventType == eventType
        );

        preset.Variables.Should().Equal(placeholders);
    }
}
