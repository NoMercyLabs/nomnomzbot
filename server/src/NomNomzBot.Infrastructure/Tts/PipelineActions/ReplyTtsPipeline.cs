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

namespace NomNomzBot.Infrastructure.Tts.PipelineActions;

/// <summary>Runs one resolved chat reply through the existing play_tts pipeline action.</summary>
internal static class ReplyTtsPipeline
{
    private const string Graph =
        """{"steps":[{"action":{"type":"play_tts","text":"{tts.reply}"}}]}""";

    public static Task<PipelineExecutionResult> QueueAsync(
        IPipelineEngine engine,
        Guid broadcasterId,
        string userId,
        string displayName,
        string? platform,
        string text,
        CancellationToken cancellationToken
    ) =>
        engine.ExecuteAsync(
            new PipelineRequest
            {
                BroadcasterId = broadcasterId,
                PipelineJson = Graph,
                TriggeredByUserId = userId,
                TriggeredByDisplayName = displayName,
                TriggeredByPlatform = platform,
                InitialVariables = new() { ["tts.reply"] = text },
            },
            cancellationToken
        );
}
