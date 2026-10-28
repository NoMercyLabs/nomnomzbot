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
    Task SendRedemptionActivityAsync(
        string broadcasterId,
        RedemptionActivityDto dto,
        CancellationToken ct = default
    );
    Task SendRedemptionStatusChangedAsync(
        string broadcasterId,
        RedemptionStatusChangedDto dto,
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
    Task SendObsLiveStateAsync(
        string broadcasterId,
        ObsLiveStateDto dto,
        CancellationToken ct = default
    );
    Task SendObsStateAsync(
        string broadcasterId,
        ObsStateChangedDto dto,
        CancellationToken ct = default
    );
    Task SendVtsStatusAsync(string broadcasterId, VtsStatusDto dto, CancellationToken ct = default);

    /// <summary>Sends a suspension change to the channel group and to every connection of <paramref name="userIds"/>.</summary>
    Task SendChannelSuspensionChangedAsync(
        string broadcasterId,
        IReadOnlyList<string> userIds,
        ChannelSuspensionChangedDto dto,
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
    )
    {
        ChannelEventDto evt = new(
            method,
            broadcasterId,
            userId,
            userDisplayName,
            data,
            _timeProvider.GetUtcNow().ToString("O")
        );
        Task classPush = _hub
            .Clients.Group(
                DashboardEventClasses.ClassGroup(
                    broadcasterId,
                    DashboardEventClasses.ClassForChannelEvent(method)
                )
            )
            .ChannelEvent(evt);
        if (ViewerChatEvents.IsMirrored(method))
            return Task.WhenAll(
                classPush,
                _hub.Clients.Group(ViewerChatEvents.Group(broadcasterId))
                    .ChannelEvent(evt with { Data = ViewerChatEvents.ViewerSafe(data) })
            );

        if (!DashboardEventClasses.IsViewerMusicEvent(method))
            return classPush;

        return Task.WhenAll(
            classPush,
            _hub.Clients.Group(DashboardEventClasses.ViewerMusicGroup(broadcasterId))
                .ChannelEvent(evt)
        );
    }

    public Task SendChatMessageAsync(
        string broadcasterId,
        DashboardChatMessageDto dto,
        CancellationToken ct = default
    ) =>
        Task.WhenAll(
            ClassGroup(broadcasterId, DashboardEventClasses.Chat).ChatMessage(dto),
            _hub.Clients.Group(ViewerChatEvents.Group(broadcasterId)).ChatMessage(dto)
        );

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

    public Task SendRedemptionActivityAsync(
        string broadcasterId,
        RedemptionActivityDto dto,
        CancellationToken ct = default
    ) => ClassGroup(broadcasterId, DashboardEventClasses.Activity).RedemptionActivity(dto);

    public Task SendRedemptionStatusChangedAsync(
        string broadcasterId,
        RedemptionStatusChangedDto dto,
        CancellationToken ct = default
    ) => BaseGroup(broadcasterId).RedemptionStatusChanged(dto);

    public Task SendMusicStateAsync(
        string broadcasterId,
        MusicStateDto dto,
        CancellationToken ct = default
    ) =>
        Task.WhenAll(
            ClassGroup(broadcasterId, DashboardEventClasses.Music).MusicStateChanged(dto),
            _hub.Clients.Group(DashboardEventClasses.ViewerMusicGroup(broadcasterId))
                .MusicStateChanged(dto)
        );

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

    public Task SendObsLiveStateAsync(
        string broadcasterId,
        ObsLiveStateDto dto,
        CancellationToken ct = default
    ) => BaseGroup(broadcasterId).ObsLiveStateChanged(dto);

    public Task SendObsStateAsync(
        string broadcasterId,
        ObsStateChangedDto dto,
        CancellationToken ct = default
    ) => BaseGroup(broadcasterId).ObsStateChanged(dto);

    public Task SendVtsStatusAsync(
        string broadcasterId,
        VtsStatusDto dto,
        CancellationToken ct = default
    ) => BaseGroup(broadcasterId).VtsStatusChanged(dto);

    // A suspended channel refuses JoinChannel, so a dashboard opened after the suspension is in no channel
    // group: the user-targeted push is what lets it hear the reinstatement.
    public async Task SendChannelSuspensionChangedAsync(
        string broadcasterId,
        IReadOnlyList<string> userIds,
        ChannelSuspensionChangedDto dto,
        CancellationToken ct = default
    )
    {
        await BaseGroup(broadcasterId).ChannelSuspensionChanged(dto);

        if (userIds.Count > 0)
            await _hub.Clients.Users(userIds).ChannelSuspensionChanged(dto);
    }

    private IDashboardClient BaseGroup(string broadcasterId) =>
        _hub.Clients.Group(DashboardEventClasses.BaseGroup(broadcasterId));

    private IDashboardClient ClassGroup(string broadcasterId, string eventClass) =>
        _hub.Clients.Group(DashboardEventClasses.ClassGroup(broadcasterId, eventClass));
}
