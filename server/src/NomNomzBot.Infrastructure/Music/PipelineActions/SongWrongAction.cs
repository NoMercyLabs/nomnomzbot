// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.PipelineActions;

/// <summary>
/// Wrong-song action (the legacy <c>!wrongsong</c>): undoes the TRIGGERING user's most recent song request.
/// Requests are attributed by display name — exactly how <c>song_request</c> enqueues them.
///
/// <para>
/// Given a song code (<c>!wrongsong K7QM</c>) it retracts THAT request. Without one it falls back to the
/// caller's most recent — which is ambiguous the moment somebody has two in the queue, and is exactly the
/// guesswork the code exists to remove. A code that belongs to someone else is refused: naming a request
/// must not become a way to remove other people's.
/// </para>
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
/// retracted track and its realtime socket auto-skipped it when it started. Here the same happens in two
/// parts: removing an entry the provider already holds arms a marker in the queue store, and
/// <c>SongRetractionSkipHandler</c> skips the track when playback reports it. The reply says "will skip"
/// only for that case.
/// </para>
///
/// Usage example:
///   { "type": "song_wrong" }
/// </summary>
public sealed class SongWrongAction : ICommandAction
{
    /// <summary>How many of the caller's newest history rows to look through for the track just retracted.</summary>
    private const int HistoryScanLimit = 50;

    private static readonly JsonSerializerOptions HistoryJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IMusicService _music;
    private readonly IApplicationDbContext _db;
    private readonly IChatProvider _chat;
    private readonly IBuiltinResponseComposer _composer;
    private readonly ILogger<SongWrongAction> _logger;

    public string ActionType => "song_wrong";

    public LocalizedText Category => new("pipeline.category.music");

    public LocalizedText Description => new("pipeline.song_wrong.description");

    public SongWrongAction(
        IMusicService music,
        IApplicationDbContext db,
        IChatProvider chat,
        IBuiltinResponseComposer composer,
        ILogger<SongWrongAction> logger
    )
    {
        _music = music;
        _db = db;
        _chat = chat;
        _composer = composer;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string broadcasterId = ctx.BroadcasterId.ToString();
        MusicQueue queue = await _music.GetQueueAsync(broadcasterId, ctx.CancellationToken);

        // "!wrongsong K7QM" — the first chat argument, the same slot every other arg-taking action reads.
        string? requestedCode = ctx.Variables.TryGetValue("args.1", out string? firstArg)
            ? SongCode.TryParse(firstArg)
            : null;

        // The queue snapshot is position-ordered; without a code the caller's LAST entry is their newest.
        int position = -1;
        MusicQueueItem? item = null;
        for (int i = 0; i < queue.Queue.Count; i++)
        {
            if (!IsCallers(queue.Queue[i], ctx))
                continue;

            if (requestedCode is null)
            {
                position = i;
                item = queue.Queue[i];
                continue;
            }

            if (string.Equals(queue.Queue[i].Code, requestedCode, StringComparison.Ordinal))
            {
                position = i;
                item = queue.Queue[i];
                break;
            }
        }

        // "!wrongsong 5" — a whole number that is not one of the caller's codes names the entry !queue
        // prints as #5. Codes win, so a code made only of digits still resolves as a code.
        if (
            (requestedCode is null || item is null)
            && ctx.Variables.TryGetValue("args.1", out string? numberArg)
            && numberArg.Length > 0
            && numberArg.All(char.IsAsciiDigit)
        )
            return await RetractByQueueNumberAsync(ctx, queue, numberArg);

        // A code that names nothing of theirs is a mistake worth reporting, not a silent fallback to
        // "remove your latest" — that would retract a different song than the one they asked for.
        if (requestedCode is not null && item is null)
        {
            await SendAsync(
                ctx,
                $"@{ctx.TriggeredByChatName} No request of yours with code {requestedCode}."
            );
            ctx.ShouldStop = true;
            return ActionResult.Success($"no request matching code {requestedCode}");
        }

        if (item is null)
            return await UndoThePlayingTrackAsync(ctx, queue);

        static bool IsCallers(MusicQueueItem queued, PipelineExecutionContext context) =>
            string.Equals(
                queued.RequestedBy,
                context.TriggeredByDisplayName,
                StringComparison.OrdinalIgnoreCase
            );

        RequestRetraction retraction = await _music.RetractRequestAsync(
            broadcasterId,
            position,
            ctx.CancellationToken
        );
        if (retraction == RequestRetraction.NotRemoved)
            return ActionResult.Failure("failed to remove the request from the queue");

        await RemoveNewestHistoryAsync(ctx, item.TrackName, item.Artist);
        await SendAsync(
            ctx,
            retraction == RequestRetraction.RemovedWillSkip
                ? $"@{ctx.TriggeredByChatName} Will skip {item.TrackName} by {item.Artist} when it starts."
                : $"@{ctx.TriggeredByChatName} Removed {item.TrackName} by {item.Artist} from the queue."
        );
        return ActionResult.Success($"removed: {item.TrackName}");
    }

    /// <summary>
    /// <c>!wrongsong N</c>: removes the entry at the number <c>!queue</c> shows (<see cref="QueueNumbering"/>),
    /// only when the caller requested it. A number outside the queue or naming someone else's request is
    /// refused and removes nothing. Removal goes through the same retract call as the code path, so a song
    /// Spotify already holds is skipped when it starts.
    /// </summary>
    private async Task<ActionResult> RetractByQueueNumberAsync(
        PipelineExecutionContext ctx,
        MusicQueue queue,
        string numberArg
    )
    {
        // A number too large for int is outside any queue.
        int number = int.TryParse(numberArg, out int parsed) ? parsed : 0;
        int? position = QueueNumbering.PositionOf(number, queue.Queue.Count);
        Dictionary<string, string> variables = new()
        {
            ["user"] = ctx.TriggeredByChatName,
            ["request.position"] = numberArg,
        };

        if (position is null)
        {
            await SendSlotAsync(
                ctx,
                BuiltinResponseSlots.Skip.NoRequest,
                "@{user} There is no request #{request.position} in the queue.",
                variables
            );
            ctx.ShouldStop = true;
            return ActionResult.Success($"no queue entry #{numberArg}");
        }

        MusicQueueItem item = queue.Queue[position.Value];
        if (
            !string.Equals(
                item.RequestedBy,
                ctx.TriggeredByDisplayName,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            await SendSlotAsync(
                ctx,
                BuiltinResponseSlots.Skip.NotYours,
                "@{user} #{request.position} is not your request.",
                variables
            );
            ctx.ShouldStop = true;
            return ActionResult.Success($"queue entry #{numberArg} is not the caller's");
        }

        RequestRetraction retraction = await _music.RetractRequestAsync(
            ctx.BroadcasterId.ToString(),
            position.Value,
            ctx.CancellationToken
        );
        if (retraction == RequestRetraction.NotRemoved)
            return ActionResult.Failure("failed to remove the request from the queue");

        await RemoveNewestHistoryAsync(ctx, item.TrackName, item.Artist);
        variables["track.name"] = item.TrackName;
        variables["track.artist"] = item.Artist;
        await SendSlotAsync(
            ctx,
            BuiltinResponseSlots.Skip.Removed,
            "@{user} Removed your request: {track.name} by {track.artist}",
            variables
        );
        return ActionResult.Success($"removed: {item.TrackName}");
    }

    private async Task SendSlotAsync(
        PipelineExecutionContext ctx,
        string slot,
        string neutralFallback,
        IReadOnlyDictionary<string, string> variables
    )
    {
        string text = await _composer.ComposeAsync(
            new()
            {
                BroadcasterId = ctx.BroadcasterId,
                Personality = PersonalityTone.Informative,
                BuiltinKey = BuiltinResponseSlots.Skip.Key,
                Slot = slot,
                NeutralFallback = neutralFallback,
                Variables = variables,
            },
            ctx.CancellationToken
        );
        await SendAsync(ctx, text);
    }

    /// <summary>
    /// Answers as a reply to the triggering chat message, like the legacy bot. A platform that rejects the
    /// reply form (a deleted parent message, no connection) falls back to a plain message — the mention is
    /// already in the text, so the viewer is still reached.
    /// </summary>
    private async Task SendAsync(PipelineExecutionContext ctx, string text)
    {
        bool replied = await _chat.SendReplyAsync(
            ctx.BroadcasterId,
            ctx.MessageId,
            text,
            ctx.CancellationToken
        );
        if (replied)
        {
            ctx.RepliedToChat = true;
            return;
        }

        ctx.RepliedToChat = await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            text,
            ctx.CancellationToken
        );
    }

    /// <summary>
    /// Removes the caller's newest song-request history record of the given track and artist — the one just
    /// retracted or skipped. Returns what it removed, or <c>null</c> when there was none.
    /// </summary>
    private async Task<SongRequestHistory?> RemoveNewestHistoryAsync(
        PipelineExecutionContext ctx,
        string? trackName,
        string? artist
    )
    {
        List<Domain.Platform.Entities.Record> rows = await _db
            .Records.Where(r =>
                r.BroadcasterId == ctx.BroadcasterId
                && r.UserId == ctx.TriggeredByUserId
                && r.RecordType == SongRequestHistory.RecordType
            )
            .OrderByDescending(r => r.Id)
            .Take(HistoryScanLimit)
            .ToListAsync(ctx.CancellationToken);

        foreach (Domain.Platform.Entities.Record row in rows)
        {
            SongRequestHistory? history = Deserialize(row.Data);
            if (history is null)
                continue;
            if (
                !string.Equals(history.TrackName, trackName, StringComparison.Ordinal)
                || !string.Equals(history.Artist, artist, StringComparison.Ordinal)
            )
                continue;

            _db.Records.Remove(row);
            await _db.SaveChangesAsync(ctx.CancellationToken);
            return history;
        }

        return null;
    }

    private SongRequestHistory? Deserialize(string data)
    {
        try
        {
            return JsonSerializer.Deserialize<SongRequestHistory>(data, HistoryJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "song_wrong: unreadable song-request history row skipped");
            return null;
        }
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
            // Neither queued nor playing: nothing to skip. A request on record played in an earlier stream
            // (no stream boundary exists here), so it is never reported as "already played".
            await SendAsync(
                ctx,
                $"@{ctx.TriggeredByChatName} You haven't requested any songs to retract."
            );
            ctx.ShouldStop = true;
            return ActionResult.Success("no queued request for the triggering user");
        }

        Result skipped = await _music.SkipAsync(
            ctx.BroadcasterId.ToString(),
            ctx.TriggeredByUserId,
            ctx.TriggeredByPlatform,
            ctx.CancellationToken
        );
        if (!skipped.IsSuccess)
        {
            // Say so rather than staying silent: the wrong song is still playing, and a quiet failure reads
            // as "the bot ignored me" while the user waits for it to stop.
            await SendAsync(ctx, $"@{ctx.TriggeredByChatName} Failed to retract your last song.");
            _logger.LogWarning(
                "song_wrong: skip failed for {BroadcasterId}: {Error}",
                ctx.BroadcasterId,
                skipped.ErrorMessage
            );
            return ActionResult.Failure("failed to skip the playing request");
        }

        await RemoveNewestHistoryAsync(ctx, playing!.TrackName, playing.Artist);
        await SendAsync(
            ctx,
            $"@{ctx.TriggeredByChatName} Skipped {playing.TrackName} by {playing.Artist}."
        );
        return ActionResult.Success($"skipped: {playing.TrackName}");
    }
}
