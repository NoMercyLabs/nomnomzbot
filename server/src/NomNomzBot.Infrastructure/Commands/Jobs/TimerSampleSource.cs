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

using NomNomzBot.Application.Contracts.CustomCode;
using Timer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Commands.Jobs;

/// <summary>
/// A firing timer as a test-run trigger, with the variables <see cref="TimerService"/> hands a timer's pipeline.
/// A timer has no chatter: the channel is the actor.
/// </summary>
public sealed class TimerSampleSource : ITriggerSampleSource
{
    /// <summary>The key a timer's pipeline is typed and sampled under; a timer runs its pipeline directly, not through an event response.</summary>
    public const string ResponseKey = "timer";

    public TriggerSample Sample(DateTimeOffset now)
    {
        Timer timer = new()
        {
            Name = "hydrate-reminder",
            Messages = ["Time to drink some water!", "Stretch your legs for a minute."],
            NextMessageIndex = 0,
        };

        return new(
            ResponseKey,
            ResponseKey,
            "100000001",
            "SampleChannel",
            TimerService.BuildVariables(timer)
        );
    }
}
