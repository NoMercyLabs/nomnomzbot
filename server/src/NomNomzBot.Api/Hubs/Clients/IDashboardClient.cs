// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;

namespace NomNomzBot.Api.Hubs.Clients;

public interface IDashboardClient
{
    Task ChatMessage(DashboardChatMessageDto message);
    Task ChannelEvent(ChannelEventDto evt);
    Task PermissionChanged(PermissionChangedDto evt);
    Task MusicStateChanged(MusicStateDto state);
    Task ModAction(ModActionDto action);
    Task CommandExecuted(CommandExecutedDto evt);
    Task RewardRedeemed(RewardRedeemedDto evt);
    Task StreamStatusChanged(StreamStatusDto status);
    Task AlertTriggered(AlertDto alert);
    Task StreamInfoChanged(StreamInfoChangedDto evt);
    Task RewardChanged(RewardChangedDto evt);
}
