// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The shape of one <c>moderation_action</c> row's JSON payload — what the dashboard's action log, the
/// banned-viewers list and the user-context card read back.
/// </summary>
internal sealed class ModerationActionData
{
    public string Action { get; set; } = string.Empty;
    public string? TargetUserId { get; set; }
    public string? TargetUsername { get; set; }
    public string? Reason { get; set; }
    public int? DurationSeconds { get; set; }
}

/// <summary>
/// Writes the one <c>moderation_action</c> row every moderation action leaves in the mod log. The dashboard
/// (<c>ModerationService</c>) and the pipeline actions on a non-Twitch tenant share this single writer, so a
/// ban looks the same in the log whichever surface issued it. Call it only AFTER the platform accepted the
/// action — a row for an action that was only attempted is a fake the dashboard then displays.
/// </summary>
internal static class ModerationActionRecord
{
    public const string RecordType = "moderation_action";

    public static async Task<Result<ModerationActionResult>> WriteAsync(
        IApplicationDbContext db,
        Guid tenantId,
        string action,
        string targetUserId,
        string? reason,
        int? durationSeconds,
        string? moderatorId,
        CancellationToken cancellationToken
    )
    {
        bool channelExists = await db.Channels.AnyAsync(c => c.Id == tenantId, cancellationToken);
        if (!channelExists)
            return Errors.ChannelNotFound<ModerationActionResult>(tenantId.ToString());

        // targetUserId is the platform user id passed to the platform API — resolve the username via TwitchUserId.
        User? targetUser = await db.Users.FirstOrDefaultAsync(
            u => u.TwitchUserId == targetUserId,
            cancellationToken
        );

        ModerationActionData actionData = new()
        {
            Action = action,
            TargetUserId = targetUserId,
            TargetUsername = targetUser?.Username,
            Reason = reason,
            DurationSeconds = durationSeconds,
        };

        db.Records.Add(
            new()
            {
                BroadcasterId = tenantId,
                RecordType = RecordType,
                Data = JsonSerializer.Serialize(actionData),
                UserId = moderatorId ?? tenantId.ToString(),
            }
        );
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new ModerationActionResult(true, $"{action} applied successfully."));
    }
}
