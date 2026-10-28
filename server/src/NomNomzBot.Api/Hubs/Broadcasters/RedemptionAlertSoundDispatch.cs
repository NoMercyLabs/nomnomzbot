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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Plays a widget's configured alert sound on the shared overlay audio bus (S058b). Any enabled widget
/// subscribed to <c>reward_redeemed</c> whose <c>Settings["soundClipId"]</c> (widget-settings schema, e.g.
/// <c>redemption_alert</c>) names a real sound clip gets that clip pushed as a <c>PlaySound</c> right alongside
/// the alert's <c>WidgetEvent</c>. Fail-quiet by design: a blank/absent setting, or a setting pointing at a
/// clip that no longer exists or is disabled, is not an error — the alert simply plays silently, matching a
/// human unplugging a sound they no longer want rather than a broken feature.
/// </summary>
internal static class RedemptionAlertSoundDispatch
{
    private const string SoundSettingKey = "soundClipId";

    public static async Task PlayConfiguredSoundAsync(
        IApplicationDbContext db,
        ISoundClipService soundClips,
        IWidgetNotifier notifier,
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        if (broadcasterId == Guid.Empty)
            return;

        // EventSubscriptions is a JSON-converted column — the enabled/channel filter runs server-side; the
        // subscription membership check runs client-side over that already-narrow result set (matches
        // GoalWidgetEventHandler's read for the same reason: List<string>.Contains cannot translate to SQL).
        List<Widget> candidates = await db
            .Widgets.AsNoTracking()
            .Where(w => w.BroadcasterId == broadcasterId && w.IsEnabled)
            .ToListAsync(cancellationToken);

        foreach (Widget widget in candidates.Where(w => w.Wants("reward_redeemed")))
        {
            if (
                !widget.Settings.TryGetValue(SoundSettingKey, out object? rawClipRef)
                || rawClipRef is not string clipRef
                || string.IsNullOrWhiteSpace(clipRef)
            )
                continue;

            Result<SoundPlaybackDto> resolved = await soundClips.ResolveForPlaybackAsync(
                broadcasterId,
                clipRef,
                volumeOverride: null,
                cancellationToken
            );
            if (!resolved.IsSuccess)
                continue;

            await notifier.PlaySoundAsync(
                broadcasterId.ToString(),
                new PlaySoundPayload(resolved.Value.PlaybackUrl, resolved.Value.Volume, null),
                cancellationToken
            );
        }
    }
}
