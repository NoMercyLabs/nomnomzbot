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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PlatformDefaults.Dtos;
using NomNomzBot.Application.PlatformDefaults.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Tts.Entities;

namespace NomNomzBot.Infrastructure.PlatformDefaults;

/// <summary>
/// See <see cref="ITtsVoiceDefaultsAdminService"/>. Moves the catalogue's <c>IsDefault</c> flag; the channel
/// read model and the dispatcher resolve the flagged voice on every read for each channel whose config still
/// follows the platform default (a null <c>DefaultVoiceId</c>), so a save speaks on the next utterance.
/// </summary>
public sealed class TtsVoiceDefaultsAdminService(IApplicationDbContext db, TimeProvider clock)
    : ITtsVoiceDefaultsAdminService
{
    private const string AuditFamily = "tts_voice";
    private const string TargetKey = "default_voice";

    // Every channel starts on the client_edge plane, which needs no provider key; a default from a keyed
    // provider would leave a following channel unable to speak at all.
    private const string RequiredProvider = "edge";

    public async Task<Result<TtsVoiceDefaultDto>> GetAsync(CancellationToken ct = default)
    {
        TtsVoice? current = await CurrentAsync(ct);
        if (current is null)
            return Result.Failure<TtsVoiceDefaultDto>(
                "The voice catalogue marks no voice as the platform default.",
                "NOT_FOUND"
            );
        return Result.Success(await ToDtoAsync(current, ct));
    }

    public async Task<Result<PlatformDefaultBlastRadiusDto>> PreviewAsync(
        TtsVoiceDefaultChange change,
        CancellationToken ct = default
    )
    {
        Result<TtsVoice> target = await ValidateAsync(change.VoiceId, ct);
        if (target.IsFailure)
            return target.WithValue<PlatformDefaultBlastRadiusDto>(null!);
        TtsVoice? current = await CurrentAsync(ct);
        return Result.Success(await CountAsync(current, target.Value, ct));
    }

    public async Task<Result<TtsVoiceDefaultDto>> SetAsync(
        SetTtsVoiceDefaultRequest request,
        Guid actorUserId,
        CancellationToken ct = default
    )
    {
        Result<TtsVoice> validated = await ValidateAsync(request.VoiceId, ct);
        if (validated.IsFailure)
            return validated.WithValue<TtsVoiceDefaultDto>(null!);
        TtsVoice target = validated.Value;
        TtsVoice? current = await CurrentAsync(ct);

        PlatformDefaultBlastRadiusDto radius = await CountAsync(current, target, ct);
        if (radius.ChannelsAffected != request.ConfirmedChannelsAffected)
            return Result.Failure<TtsVoiceDefaultDto>(
                $"The change now affects {radius.ChannelsAffected} channels, not the {request.ConfirmedChannelsAffected} you confirmed. Review it again.",
                "PREVIEW_STALE"
            );

        // One default at a time: clear every flag (an imported catalogue may carry more than one) before setting
        // the new one, in the same save as the audit row.
        List<TtsVoice> flagged = await db.TtsVoices.Where(v => v.IsDefault).ToListAsync(ct);
        foreach (TtsVoice voice in flagged)
            voice.IsDefault = false;
        target.IsDefault = true;
        PlatformDefaultAudit.Record(
            db,
            AuditFamily,
            TargetKey,
            actorUserId,
            Describe(current),
            Describe(target),
            radius.ChannelsAffected,
            clock.GetUtcNow().UtcDateTime
        );
        await db.SaveChangesAsync(ct);

        TtsVoice saved = await db.TtsVoices.AsNoTracking().SingleAsync(v => v.IsDefault, ct);
        return Result.Success(await ToDtoAsync(saved, ct));
    }

    /// <summary>The voice must exist in the catalogue and be one every channel can speak without a key.</summary>
    private async Task<Result<TtsVoice>> ValidateAsync(string voiceId, CancellationToken ct)
    {
        string trimmed = voiceId.Trim();
        TtsVoice? voice = await db.TtsVoices.FirstOrDefaultAsync(v => v.Id == trimmed, ct);
        if (voice is null)
            return Result.Failure<TtsVoice>($"Unknown voice '{trimmed}'.", "NOT_FOUND");
        if (!string.Equals(voice.Provider, RequiredProvider, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<TtsVoice>(
                $"The platform default must be an {RequiredProvider} voice: every channel can speak it without a key of its own.",
                "VALIDATION_FAILED"
            );
        return Result.Success(voice);
    }

    private async Task<PlatformDefaultBlastRadiusDto> CountAsync(
        TtsVoice? current,
        TtsVoice target,
        CancellationToken ct
    ) =>
        await PlatformDefaultBlastRadius.CountAsync(
            db,
            ChannelsWithOwnVoice(),
            valueChanges: !string.Equals(current?.Id, target.Id, StringComparison.Ordinal),
            requiresDangerConfirmation: false,
            ct
        );

    // Only a channel that picked a voice of its own keeps it; a channel with a config row that never chose (null
    // voice) and a channel with no row at all both follow the platform default.
    private IQueryable<Guid> ChannelsWithOwnVoice() =>
        db
            .TtsConfigs.IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null && c.DefaultVoiceId != null)
            .Select(c => c.BroadcasterId);

    private async Task<TtsVoice?> CurrentAsync(CancellationToken ct) =>
        await db.TtsVoices.Where(v => v.IsDefault).OrderBy(v => v.Id).FirstOrDefaultAsync(ct);

    private async Task<TtsVoiceDefaultDto> ToDtoAsync(TtsVoice voice, CancellationToken ct)
    {
        IQueryable<Guid> own = ChannelsWithOwnVoice();
        int active = await db.Channels.CountAsync(
            c => c.Status == AuthEnums.ChannelStatus.Active,
            ct
        );
        int withOwnVoice = await db.Channels.CountAsync(
            c => c.Status == AuthEnums.ChannelStatus.Active && own.Contains(c.Id),
            ct
        );
        return new(
            voice.Id,
            voice.DisplayName,
            voice.Locale,
            voice.Provider,
            active - withOwnVoice,
            withOwnVoice
        );
    }

    private static string Describe(TtsVoice? voice) => $"voice={voice?.Id ?? "-"}";
}
