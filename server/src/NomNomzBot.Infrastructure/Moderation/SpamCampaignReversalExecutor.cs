// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// What one restoration attempt actually achieved, account by account — never a single boolean, because
/// a campaign can touch many accounts and a caller must be able to tell "all of them" from "most of
/// them" (spam-defense.md §L3.0).
/// </summary>
/// <param name="Restored">Accounts the platform confirmed as unbanned/un-timed-out.</param>
/// <param name="Failed">Accounts the platform refused to restore — still actioned.</param>
public sealed record CampaignRestorationOutcome(
    IReadOnlyCollection<string> Restored,
    IReadOnlyCollection<string> Failed
)
{
    /// <summary>True only when every account was actually restored. A partial result is not a success.</summary>
    public bool IsComplete => Failed.Count == 0;
}

/// <summary>
/// Carries out a <see cref="Domain.Moderation.SpamDefense.CampaignReversal"/> for real (spam-defense.md
/// §L3.0): unbans/un-times-out every account a de-qualified campaign actioned.
///
/// <para>Split out of <see cref="SpamCorrelationService"/> for the same reason
/// <see cref="SpamEnforcementExecutor"/> is split from the decision service: judging and acting carry
/// different risk, and keeping them apart is what lets correlation stay unit-testable with this type
/// simply substituted.</para>
///
/// <para>Restoration is issued as the CHANNEL'S OWN token (the broadcaster), mirroring
/// <see cref="SpamEnforcementExecutor"/>'s own timeout: the original action was the channel's automation
/// with no dashboard operator in the loop, so the undo has to come from the same place. Every call
/// carries <see cref="SystemActorId"/> as the moderator id so the mod log and this record's audit trail
/// both say the truth — a machine reversed this, not a person.</para>
///
/// <para>Accounts are restored one at a time via <see cref="IModerationService.UnbanAsync"/> — the same
/// sequential, awaited shape <see cref="Infrastructure.Moderation.NetworkNukeService"/> already uses for
/// its own many-channel fan-out, rather than firing every call concurrently at Twitch. A failure on one
/// account never stops the rest: every account gets its own attempt, and the caller is handed back
/// exactly who succeeded and who did not.</para>
/// </summary>
public sealed class SpamCampaignReversalExecutor
{
    /// <summary>
    /// The moderator id stamped on every automatic reversal, so Twitch's mod log and this record's own
    /// audit trail both name a machine rather than implying an operator acted.
    /// </summary>
    public const string SystemActorId = "system:spam-defense-auto-reverse";

    private readonly IApplicationDbContext _db;
    private readonly IModerationService _moderation;
    private readonly ILogger<SpamCampaignReversalExecutor> _logger;

    public SpamCampaignReversalExecutor(
        IApplicationDbContext db,
        IModerationService moderation,
        ILogger<SpamCampaignReversalExecutor> logger
    )
    {
        _db = db;
        _moderation = moderation;
        _logger = logger;
    }

    /// <summary>
    /// Restore every account in <paramref name="accountIds"/>. Never throws on a platform failure — each
    /// account's outcome is reported back instead, so the caller can decide honestly whether the
    /// campaign is actually clear.
    /// </summary>
    public async Task<CampaignRestorationOutcome> RestoreAsync(
        Guid broadcasterId,
        IReadOnlyCollection<string> accountIds,
        CancellationToken ct = default
    )
    {
        if (accountIds.Count == 0)
            return new CampaignRestorationOutcome([], []);

        Guid ownerUserId = await _db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => c.OwnerUserId)
            .FirstOrDefaultAsync(ct);

        if (ownerUserId == Guid.Empty)
        {
            _logger.LogWarning(
                "Spam defence could not restore {Count} account(s) in {BroadcasterId}: channel no longer exists.",
                accountIds.Count,
                broadcasterId
            );
            return new CampaignRestorationOutcome([], [.. accountIds]);
        }

        List<string> restored = [];
        List<string> failed = [];
        foreach (string accountId in accountIds)
        {
            Result<ModerationActionResult> result = await _moderation.UnbanAsync(
                broadcasterId.ToString(),
                ownerUserId,
                accountId,
                SystemActorId,
                ct
            );

            if (result.IsSuccess)
            {
                restored.Add(accountId);
            }
            else
            {
                failed.Add(accountId);
                _logger.LogWarning(
                    "Spam defence could not restore {Account} in {BroadcasterId}: {Error}",
                    accountId,
                    broadcasterId,
                    result.ErrorMessage
                );
            }
        }

        return new CampaignRestorationOutcome(restored, failed);
    }
}
