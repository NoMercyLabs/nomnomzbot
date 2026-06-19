// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Transport;

public interface ITwitchChatService
{
    Task SendMessageAsync(string channelId, string message, CancellationToken ct = default);
    Task SendReplyAsync(
        string channelId,
        string replyToMessageId,
        string message,
        CancellationToken ct = default
    );
    Task JoinChannelAsync(string channelName, CancellationToken ct = default);
    Task LeaveChannelAsync(string channelName, CancellationToken ct = default);
}
