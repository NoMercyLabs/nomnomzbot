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

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Chat.Dtos;

namespace NomNomzBot.Api.Hubs.Clients;

public interface IDashboardClient
{
    Task ChatMessage(DashboardChatMessageDto message);

    /// <summary>A line, deletion or clear from an off-bot channel this connection reads.</summary>
    Task ExternalChatEvent(ExternalChatEvent chatEvent);
    Task ChannelEvent(ChannelEventDto evt);
    Task PermissionChanged(PermissionChangedDto evt);
    Task MusicStateChanged(MusicStateDto state);
    Task ModAction(ModActionDto action);
    Task CommandExecuted(CommandExecutedDto evt);
    Task RewardRedeemed(RewardRedeemedDto evt);
    Task RedemptionActivity(RedemptionActivityDto evt);
    Task RedemptionStatusChanged(RedemptionStatusChangedDto evt);
    Task StreamStatusChanged(StreamStatusDto status);
    Task AlertTriggered(AlertDto alert);
    Task StreamInfoChanged(StreamInfoChangedDto evt);
    Task RewardChanged(RewardChangedDto evt);
    Task ConfigChanged(ConfigChangedDto evt);
    Task ObsBridgeStateChanged(ObsBridgeStateDto evt);
    Task ObsLiveStateChanged(ObsLiveStateDto evt);
    Task ObsStateChanged(ObsStateChangedDto evt);
    Task VtsStatusChanged(VtsStatusDto evt);
    Task ChannelSuspensionChanged(ChannelSuspensionChangedDto evt);
}
