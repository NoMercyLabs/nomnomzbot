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

using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Tells chat when the overlay player could not play a song and the queue skipped it. The requester is the
/// request still on its way to the player (it failed before starting) or the one playing (it failed
/// mid-song); the failure event is published before the playback change that drops it, so both still hold it.
/// </summary>
public sealed class SongPlaybackFailedChatNotice : IEventHandler<SongPlaybackFailedEvent>
{
    private readonly IChatProvider _chat;
    private readonly ISongRequestQueueStore _queueStore;
    private readonly IPlaybackStateReader _playbackState;

    public SongPlaybackFailedChatNotice(
        IChatProvider chat,
        ISongRequestQueueStore queueStore,
        IPlaybackStateReader playbackState
    )
    {
        _chat = chat;
        _queueStore = queueStore;
        _playbackState = playbackState;
    }

    public async Task HandleAsync(
        SongPlaybackFailedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        SongRequestEntry? request = RequestFor(@event.BroadcasterId.ToString(), @event.TrackUri);
        string title = request?.TrackName is { Length: > 0 } requested
            ? requested
            : @event.TrackName;
        string message =
            $"\"{title}\" could not be played (the video can't play in the overlay player), so song requests moved on to the next one.";
        string? requester = PlayingRequester.Of(
            _queueStore,
            _playbackState.StateOf(@event.BroadcasterId),
            @event.BroadcasterId.ToString(),
            @event.TrackUri
        );
        if (requester is not null)
            message = $"@{requester} {message}";

        await _chat.SendMessageAsync(@event.BroadcasterId, message, cancellationToken);
    }

    private SongRequestEntry? RequestFor(string broadcasterId, string trackUri)
    {
        SongRequestEntry? inFlight = _queueStore.GetInFlight(broadcasterId);
        if (string.Equals(inFlight?.TrackUri, trackUri, StringComparison.Ordinal))
            return inFlight;

        SongRequestEntry? playing = _queueStore.GetPlayingRequest(broadcasterId);
        return string.Equals(playing?.TrackUri, trackUri, StringComparison.Ordinal)
            ? playing
            : null;
    }
}
