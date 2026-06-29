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
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Domain.CustomEvents.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.CustomEvents;

internal sealed class CustomDataIngestService : ICustomDataIngestService
{
    private const int MaxRawPayloadBytes = 64 * 1024; // 64 KB raw cap (spec D4)

    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly ICacheService _cache;

    public CustomDataIngestService(
        IApplicationDbContext db,
        IEventBus eventBus,
        ICacheService cache
    )
    {
        _db = db;
        _eventBus = eventBus;
        _cache = cache;
    }

    public async Task<Result> IngestAsync(
        Guid broadcasterId,
        string sourceName,
        string rawPayload,
        CancellationToken ct = default
    )
    {
        Domain.CustomEvents.Entities.CustomDataSource? source =
            await _db.CustomDataSources.FirstOrDefaultAsync(
                s =>
                    s.BroadcasterId == broadcasterId
                    && s.Name == sourceName
                    && s.IsEnabled
                    && s.DeletedAt == null,
                ct
            );

        if (source is null)
            return Result.Failure(
                $"Active custom data source '{sourceName}' not found.",
                "SOURCE_NOT_FOUND"
            );

        // Clamp raw payload size
        string boundedRaw =
            rawPayload.Length > MaxRawPayloadBytes ? rawPayload[..MaxRawPayloadBytes] : rawPayload;

        // Extract named fields from the raw payload via JSONPath
        Dictionary<string, string> fields = ExtractFields(boundedRaw, source.FieldMapJson);

        // Stamp last-received and persist
        source.LastReceivedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Update the latest-value cache (D4): TTL 24 h — transient fast-access store
        string cacheKey = $"customdata:{broadcasterId}:{sourceName}";
        await _cache.SetAsync(
            cacheKey,
            new CustomDataLatestValue(sourceName, fields, boundedRaw, source.LastReceivedAt.Value),
            TimeSpan.FromHours(24),
            ct
        );

        // Publish the domain event — fires pipeline triggers and feeds overlays
        CustomDataReceivedEvent domainEvent = new()
        {
            BroadcasterId = broadcasterId,
            SourceName = sourceName,
            Fields = fields,
            RawPayload = boundedRaw,
        };
        await _eventBus.PublishAsync(domainEvent, ct);

        return Result.Success();
    }

    private static Dictionary<string, string> ExtractFields(string rawPayload, string fieldMapJson)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string>? fieldMap = null;
        try
        {
            fieldMap = JsonConvert.DeserializeObject<Dictionary<string, string>>(fieldMapJson);
        }
        catch
        {
            // Malformed field-map — return empty (caller gets raw only)
        }

        if (fieldMap is null || fieldMap.Count == 0)
            return result;

        JObject? jObj = null;
        try
        {
            jObj = JObject.Parse(rawPayload);
        }
        catch
        {
            // Non-JSON payload — return empty field extraction (raw still available)
        }

        if (jObj is null)
            return result;

        foreach (KeyValuePair<string, string> mapping in fieldMap)
        {
            try
            {
                JToken? token = jObj.SelectToken(mapping.Value);
                if (token is not null)
                    result[mapping.Key] = token.ToString();
            }
            catch
            {
                // Bad JSONPath — skip this field silently
            }
        }

        return result;
    }
}

/// <summary>Shape persisted in the latest-value cache for a source (D4).</summary>
public sealed record CustomDataLatestValue(
    string SourceName,
    IReadOnlyDictionary<string, string> Fields,
    string RawPayload,
    DateTime ReceivedAt
);
