// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IO;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Assets.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Assets.Entities;
using NomNomzBot.Infrastructure.Assets.Media;

namespace NomNomzBot.Infrastructure.Assets;

internal sealed partial class ChannelAssetService : IChannelAssetService
{
    internal const long MaxAssetBytes = 8 * 1024 * 1024; // 8 MB per asset
    internal const long MaxChannelBytes = 64 * 1024 * 1024; // 64 MB per channel

    private readonly IApplicationDbContext _db;
    private readonly IChannelAssetStore _store;

    public ChannelAssetService(IApplicationDbContext db, IChannelAssetStore store)
    {
        _db = db;
        _store = store;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,50}$")]
    private static partial Regex NameSlug();

    public async Task<Result<PagedList<ChannelAssetDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        List<ChannelAssetDto> items = await _db
            .ChannelAssets.Where(a => a.BroadcasterId == broadcasterId)
            .OrderBy(a => a.Name)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(a => ToDto(a))
            .ToListAsync(ct);

        int total = await _db.ChannelAssets.CountAsync(a => a.BroadcasterId == broadcasterId, ct);

        return Result<PagedList<ChannelAssetDto>>.Success(
            new PagedList<ChannelAssetDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<ChannelAssetDto>> GetAsync(
        Guid broadcasterId,
        Guid id,
        CancellationToken ct = default
    )
    {
        ChannelAsset? asset = await _db.ChannelAssets.FirstOrDefaultAsync(
            a => a.BroadcasterId == broadcasterId && a.Id == id,
            ct
        );

        return asset is null
            ? Result<ChannelAssetDto>.Failure("Asset not found.", "NOT_FOUND")
            : Result<ChannelAssetDto>.Success(ToDto(asset));
    }

    public async Task<Result<ChannelAssetDto>> UploadAsync(
        Guid broadcasterId,
        Guid actorUserId,
        UploadChannelAssetRequest request,
        CancellationToken ct = default
    )
    {
        if (!NameSlug().IsMatch(request.Name))
            return Result<ChannelAssetDto>.Failure(
                "Asset name must be 1-50 characters of letters, digits, '-' or '_' (it becomes part of the URL).",
                "INVALID_NAME"
            );

        // Buffer the upload (the controller caps the request envelope) so we can sniff, size-check, and store.
        using MemoryStream ms = new();
        await request.Content.CopyToAsync(ms, ct);

        if (ms.Length > MaxAssetBytes)
            return Result<ChannelAssetDto>.Failure(
                $"Asset exceeds the {MaxAssetBytes / 1024 / 1024} MB size limit.",
                "SIZE_EXCEEDED"
            );

        // Sniff the leading bytes — the declared content type is never trusted.
        byte[] buffer = ms.GetBuffer();
        int sampleLength = (int)Math.Min(ms.Length, MediaSniffer.SampleLength);
        byte[] sample = new byte[sampleLength];
        Array.Copy(buffer, sample, sampleLength);

        string? sniffedMime = MediaSniffer.Sniff(sample, sampleLength);
        if (sniffedMime is null)
            return Result<ChannelAssetDto>.Failure(
                "File content is not a supported asset format (png, jpeg, gif, webp, svg, mp3, ogg, wav).",
                "INVALID_FORMAT"
            );

        ChannelAsset? existing = await _db.ChannelAssets.FirstOrDefaultAsync(
            a => a.BroadcasterId == broadcasterId && a.Name == request.Name,
            ct
        );

        // Per-channel budget: everything live except the row being replaced, plus the new payload.
        Guid? replacedId = existing?.Id;
        long usedBytes = await _db
            .ChannelAssets.Where(a =>
                a.BroadcasterId == broadcasterId && (replacedId == null || a.Id != replacedId)
            )
            .SumAsync(a => a.SizeBytes, ct);
        if (usedBytes + ms.Length > MaxChannelBytes)
            return Result<ChannelAssetDto>.Failure(
                $"Upload would exceed the channel's {MaxChannelBytes / 1024 / 1024} MB asset budget.",
                "CHANNEL_BUDGET_EXCEEDED"
            );

        ms.Position = 0;
        Result<string> storeResult = await _store.PutAsync(
            broadcasterId,
            request.FileName,
            ms,
            sniffedMime,
            ct
        );
        if (!storeResult.IsSuccess)
            return Result<ChannelAssetDto>.Failure(storeResult.ErrorMessage, storeResult.ErrorCode);

        if (existing is not null)
        {
            // Replace in place: one live row per name, the serving URL never moves.
            string oldStorageKey = existing.StorageKey;
            existing.DisplayName = request.DisplayName;
            existing.Kind = MediaSniffer.KindOf(sniffedMime);
            existing.MimeType = sniffedMime;
            existing.StorageKey = storeResult.Value;
            existing.SizeBytes = ms.Length;
            existing.CreatedByUserId = actorUserId;
            await _db.SaveChangesAsync(ct);
            await _store.DeleteAsync(oldStorageKey, ct);
            return Result<ChannelAssetDto>.Success(ToDto(existing));
        }

        ChannelAsset asset = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = broadcasterId,
            Name = request.Name,
            DisplayName = request.DisplayName,
            Kind = MediaSniffer.KindOf(sniffedMime),
            MimeType = sniffedMime,
            StorageKey = storeResult.Value,
            SizeBytes = ms.Length,
            CreatedByUserId = actorUserId,
        };

        _db.ChannelAssets.Add(asset);
        await _db.SaveChangesAsync(ct);

        return Result<ChannelAssetDto>.Success(ToDto(asset));
    }

    public async Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid id,
        Guid actorUserId,
        CancellationToken ct = default
    )
    {
        ChannelAsset? asset = await _db.ChannelAssets.FirstOrDefaultAsync(
            a => a.BroadcasterId == broadcasterId && a.Id == id,
            ct
        );

        if (asset is null)
            return Result.Failure("Asset not found.", "NOT_FOUND");

        string storageKey = asset.StorageKey;

        // Soft-delete the row first; then remove the blob.
        asset.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _store.DeleteAsync(storageKey, ct);
        return Result.Success();
    }

    public async Task<Result<ChannelAssetContent>> OpenForServingAsync(
        Guid broadcasterId,
        string name,
        CancellationToken ct = default
    )
    {
        // Anonymous route — no ambient tenant, so bypass the tenant query filter and re-apply
        // both the tenant predicate and the soft-delete predicate explicitly.
        ChannelAsset? asset = await _db
            .ChannelAssets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                a => a.BroadcasterId == broadcasterId && a.Name == name && a.DeletedAt == null,
                ct
            );

        if (asset is null)
            return Result<ChannelAssetContent>.Failure("Asset not found.", "NOT_FOUND");

        Result<System.IO.Stream> open = await _store.OpenAsync(asset.StorageKey, ct);
        if (!open.IsSuccess)
            return Result<ChannelAssetContent>.Failure(open.ErrorMessage, open.ErrorCode);

        return Result<ChannelAssetContent>.Success(
            new ChannelAssetContent(open.Value, asset.MimeType, asset.Kind, asset.SizeBytes)
        );
    }

    private static ChannelAssetDto ToDto(ChannelAsset a) =>
        new(
            a.Id,
            a.Name,
            a.DisplayName,
            a.Kind,
            a.MimeType,
            a.SizeBytes,
            a.CreatedAt,
            // Stable public serving URL (anonymous, immutable-cached); `v` busts caches on replace.
            $"/api/v1/assets/file/{a.BroadcasterId}/{a.Name}?v={a.UpdatedAt.Ticks}"
        );
}
