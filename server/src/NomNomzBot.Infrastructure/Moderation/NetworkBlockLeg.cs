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
using NomNomzBot.Domain.Moderation.Entities;
using RecordEntity = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// One tenant leg of a network block: a <c>moderation_action</c> Record carrying <c>Origin="network_block"</c>
/// and the block id. The apply fan-out and the later enforcement both write legs in this one shape, so a lift
/// finds and reverses every leg by the block id alone.
/// </summary>
internal static class NetworkBlockLeg
{
    public const string RecordType = "moderation_action";

    public static RecordEntity Create(Guid channelId, Guid actorPrincipalId, NetworkBlock block) =>
        new()
        {
            BroadcasterId = channelId,
            RecordType = RecordType,
            Data = JsonSerializer.Serialize(
                new LegData
                {
                    Action = "block",
                    TargetUserId = block.TargetTwitchUserId,
                    Reason = block.Reason,
                    Origin = "network_block",
                    NetworkBlockId = block.Id,
                }
            ),
            UserId = actorPrincipalId.ToString(),
        };

    /// <summary>The Twitch ban reason every leg uses.</summary>
    public static string BanReason(NetworkBlock block) => block.Reason ?? "Network-wide block.";

    /// <summary>The recorded leg shape — a superset of ModerationService's action data (same JSON reader).</summary>
    private sealed class LegData
    {
        public string Action { get; set; } = null!;
        public string TargetUserId { get; set; } = null!;
        public string? Reason { get; set; }
        public string? Origin { get; set; }
        public Guid? NetworkBlockId { get; set; }
    }
}
