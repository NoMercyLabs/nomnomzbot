// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
