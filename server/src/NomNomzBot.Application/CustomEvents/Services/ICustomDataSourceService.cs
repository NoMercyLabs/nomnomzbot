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

namespace NomNomzBot.Application.CustomEvents.Services;

/// <summary>
/// CRUD management for a broadcaster's custom data sources (custom-events.md §3).
/// </summary>
public interface ICustomDataSourceService
{
    Task<Result<PagedList<CustomDataSourceDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>
    /// Autocomplete search over the channel's custom data sources by name or display name — powers the
    /// "pick a source" inputs. An empty query returns the first <paramref name="limit"/> sources ordered by
    /// display name; <paramref name="limit"/> is clamped to a safe ceiling.
    /// </summary>
    Task<Result<IReadOnlyList<CustomDataSourceOptionDto>>> SearchAsync(
        Guid broadcasterId,
        string? query,
        int limit,
        CancellationToken ct = default
    );

    Task<Result<CustomDataSourceDto>> GetAsync(
        Guid broadcasterId,
        Guid id,
        CancellationToken ct = default
    );

    Task<Result<CustomDataSourceDto>> CreateAsync(
        Guid broadcasterId,
        Guid actorUserId,
        UpsertCustomDataSourceRequest request,
        CancellationToken ct = default
    );

    Task<Result<CustomDataSourceDto>> UpdateAsync(
        Guid broadcasterId,
        Guid id,
        Guid actorUserId,
        UpsertCustomDataSourceRequest request,
        CancellationToken ct = default
    );

    Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid id,
        Guid actorUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Fires a <c>CustomDataReceivedEvent</c> from a sample payload so the streamer can wire and
    /// preview a pipeline trigger before deploying the real source.
    /// </summary>
    Task<Result> TestAsync(
        Guid broadcasterId,
        Guid id,
        string samplePayload,
        CancellationToken ct = default
    );

    Task<Result<IReadOnlyList<CustomDataSourcePresetDto>>> ListPresetsAsync(
        CancellationToken ct = default
    );
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record UpsertCustomDataSourceRequest(
    string Name,
    string DisplayName,
    string SourceKind,
    string? PresetKey,
    string? EndpointUrl,
    string? AuthSecret,
    IReadOnlyDictionary<string, string> FieldMap,
    int? PollIntervalSeconds,
    bool IsEnabled
);

/// <summary>
/// Safe read projection — the <c>AuthSecret</c> is never echoed; only its presence is surfaced.
/// </summary>
public sealed record CustomDataSourceDto(
    Guid Id,
    string Name,
    string DisplayName,
    string SourceKind,
    string? PresetKey,
    string? EndpointUrl,
    bool HasAuthSecret,
    IReadOnlyDictionary<string, string> FieldMap,
    int? PollIntervalSeconds,
    bool IsEnabled,
    DateTime? LastReceivedAt
);

public sealed record CustomDataSourcePresetDto(string Key, string DisplayName, string SourceKind);

/// <summary>Minimal option projection for pick-list / autocomplete inputs (id + name + display name).</summary>
public sealed record CustomDataSourceOptionDto(Guid Id, string Name, string DisplayName);
