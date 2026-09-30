// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Infrastructure.Content.PlatformContent.Templates;
using NomNomzBot.Infrastructure.Sound.Audio;

namespace NomNomzBot.Infrastructure.Content.PlatformContent;

/// <summary>
/// <see cref="IPlatformAudioAssetService"/> — the platform audio library. Bytes go to the platform area of the
/// same <see cref="ISoundClipStore"/> channel clips use, through the same <see cref="SoundClipAudioRules"/>, so
/// one storage mechanism and one rule set serve both. Which templates name a file is read from the stored
/// <c>sound_clip</c> payloads of every non-retired definition, drafts included, so a file cannot vanish under a
/// draft that is about to be published.
/// </summary>
public sealed class PlatformAudioAssetService(
    IApplicationDbContext db,
    IPlatformIamService iam,
    ISoundClipStore store
) : IPlatformAudioAssetService
{
    private const int MaxDisplayNameLength = 100;
    private const int MaxFileNameLength = 200;

    public async Task<Result<PagedList<PlatformAudioAssetDto>>> ListAsync(
        Guid actingPrincipalId,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(actingPrincipalId, IamPermissionKeys.ContentRead, ct);
        if (gate.IsFailure)
            return gate.WithValue<PagedList<PlatformAudioAssetDto>>(null!);

        int total = await db.PlatformAudioAssets.CountAsync(ct);
        List<PlatformAudioAsset> rows = await db
            .PlatformAudioAssets.OrderBy(a => a.DisplayName)
            .ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        Dictionary<Guid, List<string>> usedBy =
            await PlatformAudioAssetReferences.TemplatesByAssetAsync(db, ct);
        List<PlatformAudioAssetDto> items =
        [
            .. rows.Select(a => ToDto(a, usedBy.GetValueOrDefault(a.Id) ?? [])),
        ];
        return Result.Success(new PagedList<PlatformAudioAssetDto>(items, page, pageSize, total));
    }

    public async Task<Result<PlatformAudioAssetDto>> UploadAsync(
        Guid actingPrincipalId,
        UploadPlatformAudioAssetRequest request,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(actingPrincipalId, IamPermissionKeys.ContentAuthor, ct);
        if (gate.IsFailure)
            return gate.WithValue<PlatformAudioAssetDto>(null!);

        string fileName = Path.GetFileName(request.FileName.Trim());
        if (fileName.Length is 0 or > MaxFileNameLength)
            return Failure<PlatformAudioAssetDto>(
                $"The file name must be 1 to {MaxFileNameLength} characters."
            );

        string displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? Path.GetFileNameWithoutExtension(fileName)
            : request.DisplayName.Trim();
        if (displayName.Length is 0 or > MaxDisplayNameLength)
            return Failure<PlatformAudioAssetDto>(
                $"The display name must be 1 to {MaxDisplayNameLength} characters."
            );

        Result<ValidatedAudio> validated = await SoundClipAudioRules.ValidateAsync(
            request.Content,
            ct
        );
        if (validated.IsFailure)
            return validated.WithValue<PlatformAudioAssetDto>(null!);
        await using ValidatedAudio audio = validated.Value;

        string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(audio.Content, ct));
        audio.Content.Position = 0;

        PlatformAudioAsset? duplicate = await db.PlatformAudioAssets.FirstOrDefaultAsync(
            a => a.ContentHash == hash,
            ct
        );
        if (duplicate is not null)
            return Result.Failure<PlatformAudioAssetDto>(
                $"This file is already in the library as '{duplicate.DisplayName}'.",
                "ALREADY_EXISTS"
            );

        Result<string> stored = await store.PutPlatformAssetAsync(
            fileName,
            audio.Content,
            audio.MimeType,
            ct
        );
        if (stored.IsFailure)
            return stored.WithValue<PlatformAudioAssetDto>(null!);

        PlatformAudioAsset asset = new()
        {
            DisplayName = displayName,
            FileName = fileName,
            StorageKey = stored.Value,
            ContentType = audio.MimeType,
            SizeBytes = audio.Content.Length,
            DurationMs = audio.DurationMs,
            ContentHash = hash,
            UploadedByPrincipalId = actingPrincipalId,
        };
        db.PlatformAudioAssets.Add(asset);
        await db.SaveChangesAsync(ct);

        return Result.Success(ToDto(asset, []));
    }

    public async Task<Result> DeleteAsync(
        Guid actingPrincipalId,
        Guid assetId,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(actingPrincipalId, IamPermissionKeys.ContentAuthor, ct);
        if (gate.IsFailure)
            return gate;

        PlatformAudioAsset? asset = await db.PlatformAudioAssets.FirstOrDefaultAsync(
            a => a.Id == assetId,
            ct
        );
        if (asset is null)
            return Result.Failure("Audio file not found.", "NOT_FOUND");

        Dictionary<Guid, List<string>> usedBy =
            await PlatformAudioAssetReferences.TemplatesByAssetAsync(db, ct);
        if (usedBy.TryGetValue(assetId, out List<string>? templates))
            return Result.Failure(
                $"This file is used by: {string.Join(", ", templates)}. Retire those templates or pick another file in them first.",
                "ASSET_IN_USE"
            );

        string storageKey = asset.StorageKey;
        asset.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return await store.DeleteAsync(storageKey, ct);
    }

    private async Task<Result> RequireAsync(
        Guid principalId,
        string permissionKey,
        CancellationToken ct
    )
    {
        Result<bool> allowed = await iam.AuthorizePlatformAsync(
            principalId,
            permissionKey,
            null,
            false,
            null,
            ct
        );
        if (allowed.IsFailure)
            return allowed;
        return allowed.Value
            ? Result.Success()
            : Result.Failure($"Requires {permissionKey}.", "FORBIDDEN");
    }

    private static Result<T> Failure<T>(string message) =>
        Result.Failure<T>(message, "VALIDATION_FAILED");

    private static PlatformAudioAssetDto ToDto(
        PlatformAudioAsset a,
        IReadOnlyList<string> usedBy
    ) =>
        new(
            a.Id,
            a.DisplayName,
            a.FileName,
            a.ContentType,
            a.SizeBytes,
            a.DurationMs,
            a.ContentHash,
            a.UploadedByPrincipalId,
            a.CreatedAt,
            // The anonymous, range-enabled clip stream route serves this key like any channel clip's key.
            $"/api/v1/sound-clips/stream/{a.StorageKey}",
            usedBy
        );
}
