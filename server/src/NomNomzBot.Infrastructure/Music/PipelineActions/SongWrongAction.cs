// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;

namespace NomNomzBot.Infrastructure.Music.PipelineActions;

/// <summary>
/// Wrong-song action (the legacy <c>!wrongsong</c>): undoes the TRIGGERING user's most recent song request.
/// Requests are attributed by display name — exactly how <c>song_request</c> enqueues them.
///
/// <para>
/// Two cases, and the second is the one that made this look broken. If the request is still WAITING it is
/// simply dropped from the queue and never plays. If it is ALREADY PLAYING there is nothing left in the
/// pending queue to remove — the provider took it — so this used to answer "you have no queued requests to
/// remove" while the wrong song kept playing. It now skips to the next track, which is what undoing a
/// request that already started actually means.
/// </para>
///
/// <para>
/// The legacy bot did this from the other end: <c>SpotifyApiService.TryConsumeSkip(trackId)</c> marked a
/// retracted track and its realtime socket auto-skipped it when it started. We have no such socket (the
/// endpoint it used is a restricted API), so the skip happens here, at the moment the user asks for it,
/// rather than being armed and waiting.
/// </para>
///
/// Usage example:
///   { "type": "song_wrong" }
/// </summary>
public sealed class SongWrongAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IChatProvider _chat;
    private readonly ILogger<SongWrongAction> _logger;

    public string ActionType => "song_wrong";

    public LocalizedText Category => new("pipeline.category.music");

    public LocalizedText Description => new("pipeline.song_wrong.description");

    public SongWrongAction(IMusicService music, IChatProvider chat, ILogger<SongWrongAction> logger)
    {
        _music = music;
        _chat = chat;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string broadcasterId = ctx.BroadcasterId.ToString();
        MusicQueue queue = await _music.GetQueueAsync(broadcasterId, ctx.CancellationToken);

        // The queue snapshot is position-ordered; the caller's LAST entry is their newest request.
        int position = -1;
        MusicQueueItem? item = null;
        for (int i = 0; i < queue.Queue.Count; i++)
        {
            if (
                string.Equals(
                    queue.Queue[i].RequestedBy,
                    ctx.TriggeredByDisplayName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                position = i;
                item = queue.Queue[i];
            }
        }

        if (item is null)
            return await UndoThePlayingTrackAsync(ctx, queue);

        bool removed = await _music.RemoveFromQueueAsync(
            broadcasterId,
            position,
            ctx.CancellationToken
        );
        if (!removed)
            return ActionResult.Failure("failed to remove the request from the queue");

        await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            $"@{ctx.TriggeredByDisplayName} Removed your request: {item.TrackName} by {item.Artist}",
            ctx.CancellationToken
        );
        return ActionResult.Success($"removed: {item.TrackName}");
    }

    /// <summary>
    /// Nothing of the caller's is waiting in the queue. If the track PLAYING RIGHT NOW is theirs, undoing it
    /// means skipping it; otherwise they genuinely have nothing to undo.
    /// </summary>
    private async Task<ActionResult> UndoThePlayingTrackAsync(
        PipelineExecutionContext ctx,
        MusicQueue queue
    )
    {
        NowPlaying? playing = queue.CurrentTrack;
        bool playingIsTheirs =
            playing is not null
            && !string.IsNullOrEmpty(playing.RequestedBy)
            && string.Equals(
                playing.RequestedBy,
                ctx.TriggeredByDisplayName,
                StringComparison.OrdinalIgnoreCase
            );

        if (!playingIsTheirs)
        {
            await _chat.SendMessageAsync(
                ctx.BroadcasterId,
                $"@{ctx.TriggeredByDisplayName} You have no queued requests to remove.",
                ctx.CancellationToken
            );
            return ActionResult.Failure("no queued request for the triggering user");
        }

        Result skipped = await _music.SkipAsync(
            ctx.BroadcasterId.ToString(),
            ctx.CancellationToken
        );
        if (!skipped.IsSuccess)
        {
            // Say so rather than staying silent: the wrong song is still playing, and a quiet failure reads
            // as "the bot ignored me" while the user waits for it to stop.
            await _chat.SendMessageAsync(
                ctx.BroadcasterId,
                $"@{ctx.TriggeredByDisplayName} Couldn't skip your track — try again in a moment.",
                ctx.CancellationToken
            );
            _logger.LogWarning(
                "song_wrong: skip failed for {BroadcasterId}: {Error}",
                ctx.BroadcasterId,
                skipped.ErrorMessage
            );
            return ActionResult.Failure("failed to skip the playing request");
        }

        await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            $"@{ctx.TriggeredByDisplayName} Skipped your request: {playing!.TrackName} by {playing.Artist}",
            ctx.CancellationToken
        );
        return ActionResult.Success($"skipped: {playing.TrackName}");
    }
}
