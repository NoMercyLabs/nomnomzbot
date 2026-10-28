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
    DateTime At,
    string? Login = null
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
