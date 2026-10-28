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
