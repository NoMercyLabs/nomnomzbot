// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Domain.Sound.Entities;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

/// <summary>
/// Installs a <c>sound_clip</c> template: streams the named platform audio file through
/// <see cref="ISoundClipService.UploadAsync"/> — the same path as the Sound page's upload — so format, size,
/// duration, the clip count and the channel's storage quota are checked again, and the bytes land under the
/// channel's own storage key. The platform file is only read here, never referenced afterwards. A slug the
/// channel already uses is installed as <c>name-2</c>, <c>name-3</c>, ….
/// </summary>
public sealed partial class SoundClipTemplateInstaller(
    IApplicationDbContext db,
    ISoundClipService clips,
    ISoundClipStore store
) : IPlatformTemplateInstaller
{
    // The limits the SoundClip row holds.
    private const int MaxNameLength = 50;
    private const int MaxDisplayNameLength = 100;
    private const int MaxTriggerWordLength = 50;
    private const int MaxCooldownSeconds = 86_400;

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex NamePattern();

    public string Kind => PlatformContentKinds.SoundClip;

    public string WriteActionKey => "sounds:write";

    public async Task<Result> ValidatePayloadAsync(
        string payloadJson,
        CancellationToken ct = default
    )
    {
        Result<SoundClipTemplatePayload> parsed =
            PlatformTemplateJson.Parse<SoundClipTemplatePayload>(payloadJson);
        if (parsed.IsFailure)
            return parsed;

        Result valid = Validate(parsed.Value);
        if (valid.IsFailure)
            return valid;

        bool assetExists = await db.PlatformAudioAssets.AnyAsync(
            a => a.Id == parsed.Value.AssetId,
            ct
        );
        return assetExists
            ? Result.Success()
            : Failure("The audio file this template names is not in the platform audio library.");
    }

    public async Task<Result<InstalledPlatformTemplateDto>> InstallAsync(
        PlatformTemplateInstall install,
        CancellationToken ct = default
    )
    {
        Result<SoundClipTemplatePayload> parsed =
            PlatformTemplateJson.Parse<SoundClipTemplatePayload>(install.PayloadJson);
        if (parsed.IsFailure)
            return parsed.WithValue<InstalledPlatformTemplateDto>(null!);
        SoundClipTemplatePayload payload = parsed.Value;

        Result valid = Validate(payload);
        if (valid.IsFailure)
            return valid.WithValue<InstalledPlatformTemplateDto>(null!);

        PlatformAudioAsset? asset = await db.PlatformAudioAssets.FirstOrDefaultAsync(
            a => a.Id == payload.AssetId,
            ct
        );
        if (asset is null)
            return Result.Failure<InstalledPlatformTemplateDto>(
                "This template's audio file is no longer in the platform audio library.",
                "NOT_FOUND"
            );

        string? name = await PlatformTemplateNames.FreeNameAsync(
            payload.Name,
            MaxNameLength,
            candidate =>
                db.SoundClips.AnyAsync(
                    c => c.BroadcasterId == install.BroadcasterId && c.Name == candidate,
                    ct
                ),
            separator: "-"
        );
        if (name is null)
            return Result.Failure<InstalledPlatformTemplateDto>(
                $"The channel already has too many clips named '{payload.Name}'.",
                "ALREADY_EXISTS"
            );

        Result<System.IO.Stream> opened = await store.OpenAsync(asset.StorageKey, ct);
        if (opened.IsFailure)
            return Result.Failure<InstalledPlatformTemplateDto>(
                "This template's audio file could not be read.",
                "NOT_FOUND"
            );

        Result<SoundClipDto> created;
        await using (System.IO.Stream audio = opened.Value)
        {
            created = await clips.UploadAsync(
                install.BroadcasterId,
                install.ActorUserId,
                new UploadSoundClipRequest(
                    name,
                    payload.DisplayName.Trim(),
                    asset.FileName,
                    asset.ContentType,
                    audio,
                    payload.DefaultVolume,
                    payload.CooldownSeconds,
                    payload.MinPermissionLevel,
                    payload.TriggerWord
                ),
                ct
            );
        }
        if (created.IsFailure)
            return created.WithValue<InstalledPlatformTemplateDto>(null!);

        SoundClip row = await db.SoundClips.FirstAsync(
            c => c.Id == created.Value.Id && c.BroadcasterId == install.BroadcasterId,
            ct
        );
        row.Stamp(
            install.Source,
            SoundClipTemplatePayload.FromEntity(row).ComputeHash(),
            DateTime.UtcNow
        );
        await db.SaveChangesAsync(ct);

        return Result.Success(new InstalledPlatformTemplateDto(Kind, row.Id, row.DisplayName));
    }

    public Task<IReadOnlyList<PlatformContentCopy>> ListCopiesAsync(
        Guid definitionId,
        CancellationToken ct = default
    ) =>
        PlatformTemplateCopies.ListAsync(
            db.SoundClips,
            definitionId,
            row => row.Id,
            row => SoundClipTemplatePayload.FromEntity(row).ComputeHash(),
            ct
        );

    public async Task<Result> UpdateCopyAsync(
        PlatformTemplateCopyUpdate update,
        CancellationToken ct = default
    )
    {
        Result<SoundClipTemplatePayload> parsed =
            PlatformTemplateJson.Parse<SoundClipTemplatePayload>(update.PayloadJson);
        if (parsed.IsFailure)
            return parsed;
        SoundClipTemplatePayload payload = parsed.Value;

        Result valid = Validate(payload);
        if (valid.IsFailure)
            return valid;

        SoundClip? row = await db
            .SoundClips.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c =>
                    c.Id == update.RowId
                    && c.BroadcasterId == update.BroadcasterId
                    && c.DeletedAt == null,
                ct
            );
        if (row is null)
            return Result.Failure("The installed sound clip no longer exists.", "NOT_FOUND");

        Guid? installedAsset = await InstalledAssetIdAsync(row, ct);
        if (installedAsset != payload.AssetId)
            return Failure(
                "This version plays a different audio file. An installed clip keeps its own copy of the audio, "
                    + "so the channel installs the template again to get the new sound."
            );

        // The name is the play_sound slug the channel's pipelines reference, so the copy keeps the name it was
        // installed under, and the channel's own on/off choice stays; the other settings follow the new version.
        Result<SoundClipDto> updated = await clips.UpdateAsync(
            update.BroadcasterId,
            row.Id,
            row.CreatedByUserId,
            new UpdateSoundClipRequest(
                payload.DisplayName.Trim(),
                payload.DefaultVolume,
                row.IsEnabled,
                payload.CooldownSeconds,
                payload.MinPermissionLevel,
                payload.TriggerWord
            ),
            ct
        );
        if (updated.IsFailure)
            return updated;

        row.Stamp(
            update.Source,
            SoundClipTemplatePayload.FromEntity(row).ComputeHash(),
            DateTime.UtcNow
        );
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// A platform audio file lives as long as a non-retired template names it: once the retired definition
    /// was the last one naming a file, the file's row is removed and its bytes deleted. Channels that installed
    /// the template keep playing — each holds its own copy of the audio.
    /// </summary>
    public async Task<Result> ReleaseRetiredAsync(Guid definitionId, CancellationToken ct = default)
    {
        HashSet<Guid> named = await PlatformAudioAssetReferences.AssetsNamedByAsync(
            db,
            definitionId,
            ct
        );
        if (named.Count == 0)
            return Result.Success();

        Dictionary<Guid, List<string>> stillNamed =
            await PlatformAudioAssetReferences.TemplatesByAssetAsync(db, ct);
        List<PlatformAudioAsset> released = await db
            .PlatformAudioAssets.Where(a => named.Contains(a.Id))
            .ToListAsync(ct);
        released.RemoveAll(a => stillNamed.ContainsKey(a.Id));
        if (released.Count == 0)
            return Result.Success();

        DateTime now = DateTime.UtcNow;
        foreach (PlatformAudioAsset asset in released)
            asset.DeletedAt = now;
        await db.SaveChangesAsync(ct);

        foreach (PlatformAudioAsset asset in released)
        {
            Result deleted = await store.DeleteAsync(asset.StorageKey, ct);
            if (deleted.IsFailure)
                return deleted;
        }
        return Result.Success();
    }

    /// <summary>The platform audio file the version this copy was installed from names.</summary>
    private async Task<Guid?> InstalledAssetIdAsync(SoundClip row, CancellationToken ct)
    {
        string? payloadJson = await db
            .PlatformContentVersions.Where(v =>
                v.DefinitionId == row.PlatformSourceDefinitionId
                && v.Version == row.PlatformSourceVersion
            )
            .Select(v => v.PayloadJson)
            .FirstOrDefaultAsync(ct);
        return payloadJson is null ? null : SoundClipTemplatePayload.ReadAssetId(payloadJson);
    }

    private static Result Validate(SoundClipTemplatePayload payload)
    {
        string name = payload.Name.Trim();
        if (name.Length == 0)
            return Failure("A sound clip template needs a name.");
        if (name.Length > MaxNameLength)
            return Failure($"The clip name is longer than {MaxNameLength} characters.");
        if (!NamePattern().IsMatch(name))
            return Failure(
                "A clip name may contain only letters, numbers, underscores and hyphens."
            );

        string displayName = payload.DisplayName.Trim();
        if (displayName.Length == 0)
            return Failure("A sound clip template needs a display name.");
        if (displayName.Length > MaxDisplayNameLength)
            return Failure($"The display name is longer than {MaxDisplayNameLength} characters.");

        if (payload.AssetId == Guid.Empty)
            return Failure("A sound clip template needs an audio file from the platform library.");
        if (payload.DefaultVolume is < 0 or > 100)
            return Failure("The volume must be between 0 and 100.");
        if (payload.CooldownSeconds is < 0 or > MaxCooldownSeconds)
            return Failure($"The cooldown must be between 0 and {MaxCooldownSeconds} seconds.");
        if (PermissionLevelNames.ToLevelValue(payload.MinPermissionLevel) is null)
            return Failure(
                $"'{payload.MinPermissionLevel}' is not a permission rung. Use one of: {string.Join(", ", PermissionLevelNames.All)}."
            );

        string? trigger = payload.TriggerWord?.Trim();
        if (!string.IsNullOrEmpty(trigger))
        {
            if (trigger.Any(char.IsWhiteSpace))
                return Failure("A trigger word must be a single word with no spaces.");
            if (trigger.Length > MaxTriggerWordLength)
                return Failure(
                    $"The trigger word is longer than {MaxTriggerWordLength} characters."
                );
        }

        return Result.Success();
    }

    private static Result Failure(string message) => Result.Failure(message, "VALIDATION_FAILED");
}
