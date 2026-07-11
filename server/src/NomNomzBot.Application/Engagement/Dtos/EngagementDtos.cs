// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Engagement.Dtos;

/// <summary>
/// The chat-hot-path signal for one inbound message while live (engagement.md §3). Built by the chat
/// hook, consumed by <c>IEngagementService.OnChatActivityAsync</c>. <c>CurrentStreamSessionId</c> is the
/// string <c>Stream.Id</c> of the live session.
/// </summary>
public sealed record EngagementSignal(
    Guid ViewerUserId,
    string ViewerExternalUserId,
    string DisplayName,
    string CurrentStreamSessionId,
    DateTime At
);

/// <summary>The per-channel engagement configuration (engagement.md §5).</summary>
public sealed record EngagementConfigDto(
    bool FirstTimeChatterEnabled,
    bool ReturningChatterEnabled,
    bool WatchStreakEnabled,
    IReadOnlyList<int> StreakMilestones,
    int GreetCooldownSeconds
);

/// <summary>Update body for the engagement configuration (engagement.md §5).</summary>
public sealed record UpdateEngagementConfigRequest(
    bool FirstTimeChatterEnabled,
    bool ReturningChatterEnabled,
    bool WatchStreakEnabled,
    IReadOnlyList<int>? StreakMilestones,
    int GreetCooldownSeconds
);
