// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Community.Dtos;

namespace NomNomzBot.Application.Community.Services;

/// <summary>
/// Bot-run chat polls: viewers vote by typing the option number in chat (every platform, no affiliate
/// gate — the custom counterpart to the Helix-native live-ops polls). One poll per channel is open at a
/// time; a viewer's LAST vote wins; an expired poll auto-closes on its next touch; closing announces the
/// result in chat and keeps the poll as history.
/// </summary>
public interface IChatPollService
{
    Task<Result<ChatPollDto>> OpenAsync(
        string broadcasterId,
        OpenChatPollRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>The channel's polls, the open one (with live tallies) first, then recent history.</summary>
    Task<Result<IReadOnlyList<ChatPollDto>>> ListAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    Task<Result<ChatPollDto>> GetAsync(
        string broadcasterId,
        Guid pollId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Closes the poll now, announces the result in chat, and clears the hot-path cache.</summary>
    Task<Result<ChatPollDto>> CloseAsync(
        string broadcasterId,
        Guid pollId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Records (or changes) a viewer's vote — called from the chat hot path when an open poll's option
    /// number is typed. Auto-closes an expired poll instead of counting the vote.
    /// </summary>
    Task RecordVoteAsync(
        Guid broadcasterId,
        Guid pollId,
        string voterProvider,
        string voterUserId,
        int optionIndex,
        CancellationToken cancellationToken = default
    );
}
