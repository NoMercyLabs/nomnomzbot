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
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Domain.CustomEvents.Entities;

namespace NomNomzBot.Infrastructure.CustomEvents;

internal sealed class CustomDataSourceService : ICustomDataSourceService
{
    private const int MaxSourcesPerChannel = 50;
    private const int MinPollIntervalSeconds = 10; // Tier-scaled floor (safe baseline)

    private readonly IApplicationDbContext _db;
    private readonly ITokenProtector _tokenProtector;
    private readonly ICustomDataIngestService _ingest;
    private readonly IEnumerable<ICustomDataSourcePreset> _presets;

    public CustomDataSourceService(
        IApplicationDbContext db,
        ITokenProtector tokenProtector,
        ICustomDataIngestService ingest,
        IEnumerable<ICustomDataSourcePreset> presets
    )
    {
        _db = db;
        _tokenProtector = tokenProtector;
        _ingest = ingest;
        _presets = presets;
    }

    public async Task<Result<PagedList<CustomDataSourceDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        int total = await _db
            .CustomDataSources.Where(s => s.BroadcasterId == broadcasterId)
            .CountAsync(ct);

        List<CustomDataSource> rows = await _db
            .CustomDataSources.Where(s => s.BroadcasterId == broadcasterId)
            .OrderBy(s => s.DisplayName)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        List<CustomDataSourceDto> dtos = rows.Select(ToDto).ToList();

        return Result<PagedList<CustomDataSourceDto>>.Success(
            new PagedList<CustomDataSourceDto>(dtos, total, pagination.Page, pagination.PageSize)
        );
    }

    public async Task<Result<IReadOnlyList<CustomDataSourceOptionDto>>> SearchAsync(
        Guid broadcasterId,
        string? query,
        int limit,
        CancellationToken ct = default
    )
    {
        int take = Math.Clamp(limit, 1, 50);

        IQueryable<CustomDataSource> sources = _db.CustomDataSources.Where(s =>
            s.BroadcasterId == broadcasterId
        );

        string term = (query ?? string.Empty).Trim().ToLowerInvariant();
        if (term.Length > 0)
            sources = sources.Where(s =>
                s.Name.ToLower().Contains(term) || s.DisplayName.ToLower().Contains(term)
            );

        List<CustomDataSourceOptionDto> options = await sources
            .OrderBy(s => s.DisplayName)
            .Take(take)
            .Select(s => new CustomDataSourceOptionDto(s.Id, s.Name, s.DisplayName))
            .ToListAsync(ct);

        return Result<IReadOnlyList<CustomDataSourceOptionDto>>.Success(options);
    }

    public async Task<Result<CustomDataSourceDto>> GetAsync(
        Guid broadcasterId,
        Guid id,
        CancellationToken ct = default
    )
    {
        CustomDataSource? source = await _db.CustomDataSources.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.Id == id,
            ct
        );

        return source is null
            ? Result<CustomDataSourceDto>.Failure("Custom data source not found.", "NOT_FOUND")
            : Result<CustomDataSourceDto>.Success(ToDto(source));
    }

    public async Task<Result<CustomDataSourceDto>> CreateAsync(
        Guid broadcasterId,
        Guid actorUserId,
        UpsertCustomDataSourceRequest request,
        CancellationToken ct = default
    )
    {
        int count = await _db.CustomDataSources.CountAsync(
            s => s.BroadcasterId == broadcasterId,
            ct
        );

        if (count >= MaxSourcesPerChannel)
            return Result<CustomDataSourceDto>.Failure(
                $"Maximum {MaxSourcesPerChannel} custom data sources per channel.",
                "LIMIT_EXCEEDED"
            );

        bool duplicate = await _db.CustomDataSources.AnyAsync(
            s => s.BroadcasterId == broadcasterId && s.Name == request.Name,
            ct
        );

        if (duplicate)
            return Result<CustomDataSourceDto>.Failure(
                $"A source named '{request.Name}' already exists.",
                "DUPLICATE_NAME"
            );

        CustomDataSource source = new()
        {
            BroadcasterId = broadcasterId,
            CreatedByUserId = actorUserId,
            Name = request.Name.ToLowerInvariant(),
            DisplayName = request.DisplayName,
            SourceKind = request.SourceKind,
            PresetKey = request.PresetKey,
            EndpointUrl = request.EndpointUrl,
            FieldMapJson = SerializeFieldMap(request.FieldMap),
            PollIntervalSeconds = ClampPollInterval(request.PollIntervalSeconds),
            IsEnabled = request.IsEnabled,
        };

        if (!string.IsNullOrWhiteSpace(request.AuthSecret))
        {
            source.AuthSecretCipher = await _tokenProtector.ProtectAsync(
                request.AuthSecret,
                new TokenProtectionContext(
                    broadcasterId.ToString(),
                    "customdata",
                    source.Id.ToString()
                ),
                ct
            );
        }

        _db.CustomDataSources.Add(source);
        await _db.SaveChangesAsync(ct);

        return Result<CustomDataSourceDto>.Success(ToDto(source));
    }

    public async Task<Result<CustomDataSourceDto>> UpdateAsync(
        Guid broadcasterId,
        Guid id,
        Guid actorUserId,
        UpsertCustomDataSourceRequest request,
        CancellationToken ct = default
    )
    {
        CustomDataSource? source = await _db.CustomDataSources.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.Id == id,
            ct
        );

        if (source is null)
            return Result<CustomDataSourceDto>.Failure(
                "Custom data source not found.",
                "NOT_FOUND"
            );

        // Name change — check for conflicts (only if actually changing)
        if (!string.Equals(source.Name, request.Name, StringComparison.OrdinalIgnoreCase))
        {
            bool duplicate = await _db.CustomDataSources.AnyAsync(
                s => s.BroadcasterId == broadcasterId && s.Name == request.Name && s.Id != id,
                ct
            );

            if (duplicate)
                return Result<CustomDataSourceDto>.Failure(
                    $"A source named '{request.Name}' already exists.",
                    "DUPLICATE_NAME"
                );

            source.Name = request.Name.ToLowerInvariant();
        }

        source.DisplayName = request.DisplayName;
        source.SourceKind = request.SourceKind;
        source.PresetKey = request.PresetKey;
        source.EndpointUrl = request.EndpointUrl;
        source.FieldMapJson = SerializeFieldMap(request.FieldMap);
        source.PollIntervalSeconds = ClampPollInterval(request.PollIntervalSeconds);
        source.IsEnabled = request.IsEnabled;

        if (!string.IsNullOrWhiteSpace(request.AuthSecret))
        {
            source.AuthSecretCipher = await _tokenProtector.ProtectAsync(
                request.AuthSecret,
                new TokenProtectionContext(
                    broadcasterId.ToString(),
                    "customdata",
                    source.Id.ToString()
                ),
                ct
            );
        }

        await _db.SaveChangesAsync(ct);

        return Result<CustomDataSourceDto>.Success(ToDto(source));
    }

    public async Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid id,
        Guid actorUserId,
        CancellationToken ct = default
    )
    {
        CustomDataSource? source = await _db.CustomDataSources.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.Id == id,
            ct
        );

        if (source is null)
            return Result.Failure("Custom data source not found.", "NOT_FOUND");

        source.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> TestAsync(
        Guid broadcasterId,
        Guid id,
        string samplePayload,
        CancellationToken ct = default
    )
    {
        CustomDataSource? source = await _db.CustomDataSources.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.Id == id,
            ct
        );

        if (source is null)
            return Result.Failure("Custom data source not found.", "NOT_FOUND");

        return await _ingest.IngestAsync(broadcasterId, source.Name, samplePayload, ct);
    }

    public Task<Result<IReadOnlyList<CustomDataSourcePresetDto>>> ListPresetsAsync(
        CancellationToken ct = default
    )
    {
        IReadOnlyList<CustomDataSourcePresetDto> dtos = _presets
            .Select(p => new CustomDataSourcePresetDto(p.Key, p.DisplayName, p.Template.SourceKind))
            .OrderBy(p => p.DisplayName)
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<CustomDataSourcePresetDto>>.Success(dtos));
    }

    private static CustomDataSourceDto ToDto(CustomDataSource source)
    {
        Dictionary<string, string> fieldMap = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            Dictionary<string, string>? parsed = JsonConvert.DeserializeObject<
                Dictionary<string, string>
            >(source.FieldMapJson);
            if (parsed is not null)
                foreach (KeyValuePair<string, string> kv in parsed)
                    fieldMap[kv.Key] = kv.Value;
        }
        catch
        {
            // Return empty on malformed JSON
        }

        return new CustomDataSourceDto(
            source.Id,
            source.Name,
            source.DisplayName,
            source.SourceKind,
            source.PresetKey,
            source.EndpointUrl,
            source.AuthSecretCipher is not null,
            fieldMap,
            source.PollIntervalSeconds,
            source.IsEnabled,
            source.LastReceivedAt
        );
    }

    private static string SerializeFieldMap(IReadOnlyDictionary<string, string> fieldMap) =>
        fieldMap.Count == 0 ? "{}" : JsonConvert.SerializeObject(fieldMap);

    private static int? ClampPollInterval(int? seconds) =>
        seconds is null ? null : Math.Max(MinPollIntervalSeconds, seconds.Value);
}
