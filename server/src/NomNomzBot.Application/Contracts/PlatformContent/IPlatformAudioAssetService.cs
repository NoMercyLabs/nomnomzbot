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

namespace NomNomzBot.Application.Contracts.PlatformContent;

/// <summary>
/// The platform audio library: audio files no channel owns, which <c>sound_clip</c> templates name. Upload runs
/// the same file rules as a channel's clip upload (format, size, duration). Every call re-asserts the caller's
/// Plane-C permission — <c>content:read</c> to list, <c>content:author</c> to upload or delete.
/// </summary>
public interface IPlatformAudioAssetService
{
    Task<Result<PagedList<PlatformAudioAssetDto>>> ListAsync(
        Guid actingPrincipalId,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<Result<PlatformAudioAssetDto>> UploadAsync(
        Guid actingPrincipalId,
        UploadPlatformAudioAssetRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Soft-deletes the file and removes its bytes. Refused with <c>ASSET_IN_USE</c> while any non-retired
    /// <c>sound_clip</c> template (any version) names it; the message lists those templates.
    /// </summary>
    Task<Result> DeleteAsync(Guid actingPrincipalId, Guid assetId, CancellationToken ct = default);
}

/// <summary>One upload: the label admins pick it by, the original file name, and the bytes.</summary>
public sealed record UploadPlatformAudioAssetRequest(
    string DisplayName,
    string FileName,
    Stream Content
);

/// <summary>
/// One platform audio file. <see cref="PreviewUrl"/> plays it in the browser; <see cref="UsedByTemplates"/>
/// names the non-retired templates that block its deletion (empty = deletable).
/// </summary>
public sealed record PlatformAudioAssetDto(
    Guid Id,
    string DisplayName,
    string FileName,
    string ContentType,
    long SizeBytes,
    int DurationMs,
    string ContentHash,
    Guid UploadedByPrincipalId,
    DateTime CreatedAt,
    string PreviewUrl,
    IReadOnlyList<string> UsedByTemplates
);
