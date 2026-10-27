// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;

namespace NomNomzBot.Infrastructure.Rewards.EventHandlers;

/// <summary>
/// Turns what a replayed reward ran into the <see cref="EventResponseOutcome"/> the dashboard reports. Only
/// steps that succeeded count, and only what viewers see or hear: a chat line, a queued voice line, and a
/// stage effect (a widget event such as the BSOD, or a sound clip). Bookkeeping steps count nothing.
/// </summary>
internal static class RewardReplayTally
{
    private const string ChatStep = "send_message";
    private const string VoiceStep = "play_tts";

    private static readonly HashSet<string> StageSteps = new(StringComparer.Ordinal)
    {
        "widget_event",
        "play_sound",
    };

    public static EventResponseOutcome FromPipeline(PipelineExecutionResult result)
    {
        int chat = 0;
        int voice = 0;
        int stage = 0;
        foreach (StepExecutionLog step in result.StepLogs.Where(s => s.Succeeded))
            switch (step.ActionType)
            {
                case ChatStep:
                    chat++;
                    break;
                case VoiceStep:
                    voice++;
                    break;
                default:
                    if (StageSteps.Contains(step.ActionType))
                        stage++;
                    break;
            }

        return new(chat, voice, stage);
    }

    /// <summary>A command or action the reward binds directly: one performed effect when it succeeded.</summary>
    public static EventResponseOutcome FromDirectRun(bool succeeded) =>
        succeeded ? new(0, 0, 1) : EventResponseOutcome.None;
}
