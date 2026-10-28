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

using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Pushes <c>chat_safety_status</c> to a channel's dashboard clients: at once when the level changes, and
/// at most once every 2 seconds while a meter moves. The meters decay with time, so a timer reads them;
/// no chat event is needed to see a raid cool down.
/// </summary>
public sealed class ChatSafetyStatusBroadcaster(
    IChatSafetyMeter meter,
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<ChatSafetyStatusBroadcaster> logger
) : BackgroundService
{
    public const string HubMethod = "chat_safety_status";

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinimumPushGap = TimeSpan.FromSeconds(2);

    private readonly Dictionary<Guid, Pushed> _last = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TickInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Chat safety status push failed; continuing");
            }
        }
    }

    public async Task TickAsync(CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        using IServiceScope scope = scopeFactory.CreateScope();
        IDashboardNotifier notifier =
            scope.ServiceProvider.GetRequiredService<IDashboardNotifier>();

        foreach (Guid channelId in meter.ActiveChannels())
        {
            ChatSafetyStatusDto status = meter.Snapshot(channelId);
            Pushed previous = _last.GetValueOrDefault(channelId, Pushed.Idle);
            if (!ShouldPush(previous, status, now))
                continue;

            _last[channelId] = new Pushed(status, now);
            await notifier.NotifyChannelAsync(channelId.ToString(), HubMethod, status, ct);
        }
    }

    private static bool ShouldPush(Pushed previous, ChatSafetyStatusDto status, DateTimeOffset now)
    {
        if (status.Level != previous.Status.Level)
            return true;

        return status != previous.Status && now - previous.At >= MinimumPushGap;
    }

    private readonly record struct Pushed(ChatSafetyStatusDto Status, DateTimeOffset At)
    {
        public static readonly Pushed Idle = new(
            new ChatSafetyStatusDto(Domain.Moderation.ChatSafety.ChatSafetyLevel.Calm, 0, 0, 0, 0),
            DateTimeOffset.MinValue
        );
    }
}
