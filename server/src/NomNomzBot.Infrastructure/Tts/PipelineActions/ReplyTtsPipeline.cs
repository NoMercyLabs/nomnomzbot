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
