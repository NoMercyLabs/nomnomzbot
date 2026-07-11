// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Platform;

/// <summary>
/// The provider-agnostic channel-ops seam (BUILD slice 3b, the `IPlatformApi` third of the platform
/// trio beside `IChatPlatform`/`IEventSource`): what a caller uses to change a channel's stream metadata
/// without knowing the platform. Deliberately WRITE-only and single-op for now — updating stream info is
/// the one channel operation the product exercises cross-platform today (2026-06-16-twitch-rebuild
/// "thin seams": abstract only where platforms actually diverge). Reads stay on the platform-native
/// clients with honest degradation.
/// </summary>
public interface IPlatformChannelApi
{
    /// <summary>
    /// Applies a stream-metadata update on the tenant channel's own platform and returns what was
    /// ACTUALLY applied (a platform may canonicalize — Twitch resolves a category name to its catalogue
    /// spelling). Fields a platform cannot represent are rejected (<c>VALIDATION_FAILED</c>), never
    /// silently dropped.
    /// </summary>
    Task<Result<PlatformStreamInfoApplied>> UpdateStreamInfoAsync(
        Guid broadcasterId,
        PlatformStreamInfoUpdate update,
        CancellationToken cancellationToken = default
    );
}

/// <summary>One platform's implementation of the channel-ops seam, keyed by its provider.</summary>
public interface IPlatformApi : IPlatformChannelApi
{
    /// <summary>The platform key this implementation serves (<c>AuthEnums.Platform</c> values).</summary>
    string Provider { get; }
}

/// <summary>The requested change — null fields are left untouched.</summary>
public sealed record PlatformStreamInfoUpdate(
    string? Title = null,
    string? CategoryName = null,
    IReadOnlyList<string>? Tags = null
);

/// <summary>What the platform actually applied (canonicalized where the platform does so).</summary>
public sealed record PlatformStreamInfoApplied(
    string? Title,
    string? CategoryName,
    IReadOnlyList<string>? Tags
);
