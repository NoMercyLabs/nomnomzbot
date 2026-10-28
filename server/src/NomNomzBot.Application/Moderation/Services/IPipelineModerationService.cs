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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// Moderation issued by a pipeline action — the channel's own automation, with no dashboard user in the loop.
/// Each call acts as the channel owner, enforces on the tenant's OWN platform (Twitch, Kick or YouTube), and
/// leaves the same moderation-log row a dashboard ban or timeout leaves. A row is written only after the
/// platform accepted the action; a refused action returns a failure and writes nothing.
/// </summary>
public interface IPipelineModerationService
{
    Task<Result<ModerationActionResult>> BanAsync(
        Guid broadcasterId,
        string targetUserId,
        string? reason,
        CancellationToken cancellationToken = default
    );

    Task<Result<ModerationActionResult>> TimeoutAsync(
        Guid broadcasterId,
        string targetUserId,
        int durationSeconds,
        string? reason,
        CancellationToken cancellationToken = default
    );
}
