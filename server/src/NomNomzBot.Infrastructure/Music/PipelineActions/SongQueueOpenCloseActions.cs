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

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Music.PipelineActions;

/// <summary>
/// Opens the channel's song queue to new requests (the <c>!openqueue</c> / dashboard-button capability,
/// now reachable from a timer, event response or custom pipeline). The music keeps playing and queued
/// songs stay; this only lifts the refusal on NEW requests.
///
/// Usage example:
///   { "type": "song_queue_open" }
/// </summary>
public sealed class SongQueueOpenAction : ICommandAction
{
    private readonly ISongRequestIntakeService _intake;

    public string ActionType => "song_queue_open";

    public LocalizedText Category => new("pipeline.category.music");

    public LocalizedText Description => new("pipeline.song_queue_open.description");

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields => [];

    public SongQueueOpenAction(ISongRequestIntakeService intake) => _intake = intake;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result<bool> opened = await _intake.SetRequestsOpenAsync(
            ctx.BroadcasterId.ToString(),
            open: true,
            ctx.CancellationToken
        );
        return opened.IsSuccess
            ? ActionResult.Success(opened.Value ? "queue opened" : "queue already open")
            : ActionResult.Failure(opened.ErrorMessage ?? "song_queue_open failed");
    }
}

/// <summary>
/// Closes the channel's song queue to new requests (the <c>!closequeue</c> / dashboard-button capability,
/// now reachable from a timer, event response or custom pipeline). The music keeps playing and queued
/// songs stay; this only refuses NEW requests.
///
/// Usage example:
///   { "type": "song_queue_close" }
/// </summary>
public sealed class SongQueueCloseAction : ICommandAction
{
    private readonly ISongRequestIntakeService _intake;

    public string ActionType => "song_queue_close";

    public LocalizedText Category => new("pipeline.category.music");

    public LocalizedText Description => new("pipeline.song_queue_close.description");

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields => [];

    public SongQueueCloseAction(ISongRequestIntakeService intake) => _intake = intake;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result<bool> closed = await _intake.SetRequestsOpenAsync(
            ctx.BroadcasterId.ToString(),
            open: false,
            ctx.CancellationToken
        );
        return closed.IsSuccess
            ? ActionResult.Success(closed.Value ? "queue closed" : "queue already closed")
            : ActionResult.Failure(closed.ErrorMessage ?? "song_queue_close failed");
    }
}
