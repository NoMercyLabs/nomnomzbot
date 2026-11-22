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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Stream.Clips;

/// <summary>
/// While a channel is live, its clips since the stream start are read once a minute (Twitch has no EventSub event for
/// a created clip). The first read of a stream fills the set of known clip ids and says nothing. Each later read
/// announces every clip that is not known yet, oldest first, through the <c>channel.clip.created</c> event response,
/// so it follows the channel's tone and switch, and publishes a <see cref="ClipCreatedEvent"/> for it. A channel that
/// goes offline drops its state. A failed read announces nothing and keeps the set.
/// </summary>
public sealed class ClipDetector : IClipDetector
{
    internal const string CreatedResponseKey = "channel.clip.created";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    // Only the worker's single loop calls ProcessAsync, so the state needs no lock.
    private readonly Dictionary<Guid, ChannelState> _states = [];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IChannelRegistry _registry;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClipDetector> _logger;

    public ClipDetector(
        IServiceScopeFactory scopeFactory,
        IChannelRegistry registry,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<ClipDetector> logger
    )
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        List<ChannelContext> live = [.. _registry.GetLiveChannels()];
        HashSet<Guid> liveIds = [.. live.Select(c => c.BroadcasterId)];
        foreach (Guid gone in _states.Keys.Where(id => !liveIds.Contains(id)).ToList())
            _states.Remove(gone);

        if (live.Count == 0)
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        ITwitchClipsApi clips = scope
            .ServiceProvider.GetRequiredService<ITwitchHelixClient>()
            .Clips;
        IEventResponseExecutor executor =
            scope.ServiceProvider.GetRequiredService<IEventResponseExecutor>();

        foreach (ChannelContext channel in live)
        {
            try
            {
                await ProcessChannelAsync(channel, clips, executor, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Clip detection for channel {Channel} failed",
                    channel.BroadcasterId
                );
            }
        }
    }

    private async Task ProcessChannelAsync(
        ChannelContext channel,
        ITwitchClipsApi clips,
        IEventResponseExecutor executor,
        CancellationToken cancellationToken
    )
    {
        if (channel.WentLiveAt is not DateTimeOffset streamStart)
            return;

        if (
            !_states.TryGetValue(channel.BroadcasterId, out ChannelState? state)
            || state.StreamStart != streamStart
        )
        {
            state = new(streamStart);
            _states[channel.BroadcasterId] = state;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (state.NextPollAt > now)
            return;

        state.NextPollAt = now + PollInterval;
        Result<TwitchPage<TwitchClip>> result = await clips.GetClipsByBroadcasterAsync(
            channel.BroadcasterId,
            new(),
            streamStart,
            cancellationToken
        );
        if (result.IsFailure)
        {
            _logger.LogDebug(
                "Clip read for channel {Channel} failed: {Error}",
                channel.BroadcasterId,
                result.ErrorCode
            );
            return;
        }

        if (!state.Learned)
        {
            state.Known.UnionWith(result.Value.Items.Select(c => c.Id));
            state.Learned = true;
            return;
        }

        List<TwitchClip> fresh =
        [
            .. result
                .Value.Items.Where(c => c.CreatedAt >= streamStart && !state.Known.Contains(c.Id))
                .OrderBy(c => c.CreatedAt),
        ];
        foreach (TwitchClip clip in fresh)
        {
            // Known before the send, like the ad warning: a failed send is not retried, so chat is never told twice.
            state.Known.Add(clip.Id);
            await AnnounceAsync(channel.BroadcasterId, clip, now, executor, cancellationToken);
        }
    }

    private async Task AnnounceAsync(
        Guid broadcasterId,
        TwitchClip clip,
        DateTimeOffset now,
        IEventResponseExecutor executor,
        CancellationToken cancellationToken
    )
    {
        await _eventBus.PublishAsync(
            new ClipCreatedEvent
            {
                BroadcasterId = broadcasterId,
                OccurredAt = now,
                ClipId = clip.Id,
                Title = clip.Title,
                CreatorName = clip.CreatorName,
                Url = clip.Url,
                CreatedAt = clip.CreatedAt,
            },
            cancellationToken
        );

        await executor.ExecuteAsync(
            broadcasterId,
            CreatedResponseKey,
            null,
            null,
            new()
            {
                ["clip.title"] = clip.Title,
                ["clip.creator"] = clip.CreatorName,
                ["clip.url"] = clip.Url,
            },
            cancellationToken
        );
    }

    private sealed class ChannelState(DateTimeOffset streamStart)
    {
        public DateTimeOffset StreamStart { get; } = streamStart;
        public DateTimeOffset NextPollAt { get; set; } = DateTimeOffset.MinValue;
        public bool Learned { get; set; }
        public HashSet<string> Known { get; } = [];
    }
}
