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
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Music.PipelineActions;

/// <summary>
/// Removes every queued song requested by one name (the <c>!clearsongs &lt;user&gt;</c> capability). The
/// song playing now keeps playing; only that requester's PENDING queue entries are removed.
///
/// Parameters:
///   requester — the requester name to match against the queue's RequestedBy (required, templated).
///
/// Usage example:
///   { "type": "song_clear_requester", "requester": "{{user.name}}" }
/// </summary>
public sealed class SongClearRequesterAction : ICommandAction
{
    private readonly IMusicService _music;

    public string ActionType => "song_clear_requester";

    public LocalizedText Category => new("pipeline.category.music");

    public LocalizedText Description => new("pipeline.song_clear_requester.description");

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "requester",
                PipelineActionFieldKind.Text,
                Required: true,
                Templated: true,
                Description: new("pipeline.song_clear_requester.requester.help")
            ),
        ];

    public SongClearRequesterAction(IMusicService music) => _music = music;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string requester = action.GetString("requester") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(requester))
            return ActionResult.Failure("song_clear_requester: requester not resolved");

        int removed = await _music.RemoveAllFromRequesterAsync(
            ctx.BroadcasterId.ToString(),
            requester,
            ctx.CancellationToken
        );
        return ActionResult.Success($"removed {removed} song(s) requested by {requester}");
    }
}
