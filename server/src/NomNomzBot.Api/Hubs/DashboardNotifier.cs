// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Dtos;

namespace NomNomzBot.Api.Hubs;

public interface IDashboardNotifier
{
    /// <summary>
    /// Pushes a generic <c>ChannelEvent</c> to the channel group. Handlers whose event HAS an acting
    /// viewer (follow/sub/cheer/raid/…) pass <paramref name="userId"/>/<paramref name="userDisplayName"/>
    /// so the activity feed can render WHO without digging into the per-event <paramref name="data"/>.
    /// </summary>
    Task NotifyChannelAsync(
        string broadcasterId,
        string method,
        object data,
        CancellationToken ct = default,
        string? userId = null,
        string? userDisplayName = null
    );
    Task SendChatMessageAsync(
        string broadcasterId,
        DashboardChatMessageDto dto,
        CancellationToken ct = default
    );
    Task SendStreamStatusAsync(
        string broadcasterId,
        StreamStatusDto dto,
        CancellationToken ct = default
    );
    Task SendCommandExecutedAsync(
        string broadcasterId,
        CommandExecutedDto dto,
        CancellationToken ct = default
    );
    Task SendAlertAsync(string broadcasterId, AlertDto dto, CancellationToken ct = default);
    Task SendModActionAsync(string broadcasterId, ModActionDto dto, CancellationToken ct = default);
    Task SendRewardRedeemedAsync(
        string broadcasterId,
        RewardRedeemedDto dto,
        CancellationToken ct = default
    );
    Task SendPermissionChangedAsync(
        string broadcasterId,
        PermissionChangedDto dto,
        CancellationToken ct = default
    );
    Task SendMusicStateAsync(
        string broadcasterId,
        MusicStateDto dto,
        CancellationToken ct = default
    );
    Task SendStreamInfoChangedAsync(
        string broadcasterId,
        StreamInfoChangedDto dto,
        CancellationToken ct = default
    );
    Task SendRewardChangedAsync(
        string broadcasterId,
        RewardChangedDto dto,
        CancellationToken ct = default
    );
    Task SendConfigChangedAsync(
        string broadcasterId,
        ConfigChangedDto dto,
        CancellationToken ct = default
    );
    Task SendObsBridgeStateAsync(
        string broadcasterId,
        ObsBridgeStateDto dto,
        CancellationToken ct = default
    );
}

public class DashboardNotifier : IDashboardNotifier
{
    private readonly IHubContext<DashboardHub, IDashboardClient> _hub;
    private readonly TimeProvider _timeProvider;

    public DashboardNotifier(
        IHubContext<DashboardHub, IDashboardClient> hub,
        TimeProvider timeProvider
    )
    {
        _hub = hub;
        _timeProvider = timeProvider;
    }

    // ── Class-routed pushes (BUILD item 5): only connections subscribed to the class receive them ──

    public Task NotifyChannelAsync(
        string broadcasterId,
        string method,
        object data,
        CancellationToken ct = default,
        string? userId = null,
        string? userDisplayName = null
    ) =>
        _hub
            .Clients.Group(
                DashboardEventClasses.ClassGroup(
                    broadcasterId,
                    DashboardEventClasses.ClassForChannelEvent(method)
                )
            )
            .ChannelEvent(
                new(
                    method,
                    broadcasterId,
                    userId,
                    userDisplayName,
                    data,
                    _timeProvider.GetUtcNow().ToString("O")
                )
            );

    public Task SendChatMessageAsync(
        string broadcasterId,
        DashboardChatMessageDto dto,
        CancellationToken ct = default
    ) => ClassGroup(broadcasterId, DashboardEventClasses.Chat).ChatMessage(dto);

    public Task SendCommandExecutedAsync(
        string broadcasterId,
        CommandExecutedDto dto,
        CancellationToken ct = default
    ) => ClassGroup(broadcasterId, DashboardEventClasses.Activity).CommandExecuted(dto);

    public Task SendModActionAsync(
        string broadcasterId,
        ModActionDto dto,
        CancellationToken ct = default
    ) => ClassGroup(broadcasterId, DashboardEventClasses.Moderation).ModAction(dto);

    public Task SendRewardRedeemedAsync(
        string broadcasterId,
        RewardRedeemedDto dto,
        CancellationToken ct = default
    ) => ClassGroup(broadcasterId, DashboardEventClasses.Activity).RewardRedeemed(dto);

    public Task SendMusicStateAsync(
        string broadcasterId,
        MusicStateDto dto,
        CancellationToken ct = default
    ) => ClassGroup(broadcasterId, DashboardEventClasses.Music).MusicStateChanged(dto);

    // ── Core pushes: always-on for every joined connection, regardless of its class set ──

    public Task SendStreamStatusAsync(
        string broadcasterId,
        StreamStatusDto dto,
        CancellationToken ct = default
    ) => BaseGroup(broadcasterId).StreamStatusChanged(dto);

    public Task SendAlertAsync(
        string broadcasterId,
        AlertDto dto,
        CancellationToken ct = default
    ) => BaseGroup(broadcasterId).AlertTriggered(dto);

    public Task SendPermissionChangedAsync(
        string broadcasterId,
        PermissionChangedDto dto,
        CancellationToken ct = default
    ) => BaseGroup(broadcasterId).PermissionChanged(dto);

    public Task SendStreamInfoChangedAsync(
        string broadcasterId,
        StreamInfoChangedDto dto,
        CancellationToken ct = default
    ) => BaseGroup(broadcasterId).StreamInfoChanged(dto);

    public Task SendRewardChangedAsync(
        string broadcasterId,
        RewardChangedDto dto,
        CancellationToken ct = default
    ) => BaseGroup(broadcasterId).RewardChanged(dto);

    public Task SendConfigChangedAsync(
        string broadcasterId,
        ConfigChangedDto dto,
        CancellationToken ct = default
    ) => BaseGroup(broadcasterId).ConfigChanged(dto);

    public Task SendObsBridgeStateAsync(
        string broadcasterId,
        ObsBridgeStateDto dto,
        CancellationToken ct = default
    ) => BaseGroup(broadcasterId).ObsBridgeStateChanged(dto);

    private IDashboardClient BaseGroup(string broadcasterId) =>
        _hub.Clients.Group(DashboardEventClasses.BaseGroup(broadcasterId));

    private IDashboardClient ClassGroup(string broadcasterId, string eventClass) =>
        _hub.Clients.Group(DashboardEventClasses.ClassGroup(broadcasterId, eventClass));
}
