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
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Tts.Interfaces;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Pulls every registered <see cref="ITtsProvider"/>'s live voice list into the <see cref="TtsVoice"/> catalogue
/// (tts.md §7, decision 7). UPSERTS by the voice's natural id — existing rows have their metadata refreshed, new
/// voices are inserted — and NEVER deletes, so a provider that returns nothing (no key configured, or a transient
/// failure) contributes nothing rather than wiping the seeded Edge voices. Captures the rich metadata the adapters
/// now surface (accent/age/styles/tags/description/preview url) as the catalogue's search/filter columns, stamping
/// styles and tags as JSON arrays the same way <c>TtsConfigService.ParseList</c> reads them back.
/// </summary>
public sealed class TtsVoiceCatalogSync : ITtsVoiceCatalogSync
{
    private readonly IEnumerable<ITtsProvider> _providers;
    private readonly IApplicationDbContext _db;
    private readonly ILogger<TtsVoiceCatalogSync> _logger;

    public TtsVoiceCatalogSync(
        IEnumerable<ITtsProvider> providers,
        IApplicationDbContext db,
        ILogger<TtsVoiceCatalogSync> logger
    )
    {
        _providers = providers;
        _db = db;
        _logger = logger;
    }

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        List<TtsVoice> existing = await _db.TtsVoices.ToListAsync(cancellationToken);
        Dictionary<string, TtsVoice> byId = existing.ToDictionary(
            v => v.Id,
            StringComparer.Ordinal
        );

        int inserted = 0;
        int updated = 0;

        foreach (ITtsProvider provider in _providers)
        {
            IReadOnlyList<TtsVoiceInfo> voices;
            try
            {
                voices = await provider.GetVoicesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "TTS catalogue sync: provider {Provider} failed to enumerate voices; skipping.",
                    provider.GetType().Name
                );
                continue;
            }

            // A keyless / unconfigured provider returns an empty list — skip it so its absence never removes rows
            // an earlier run (or the seed) wrote. Upsert-only means there is no delete path here regardless.
            if (voices.Count == 0)
                continue;

            foreach (TtsVoiceInfo info in voices)
            {
                if (byId.TryGetValue(info.Id, out TtsVoice? row))
                {
                    ApplyMetadata(row, info);
                    updated++;
                }
                else
                {
                    // IsDefault is an operator/seed choice, never provider-supplied — new rows start non-default.
                    TtsVoice created = new() { Id = info.Id, IsDefault = false };
                    ApplyMetadata(created, info);
                    _db.TtsVoices.Add(created);
                    byId[info.Id] = created;
                    inserted++;
                }
            }
        }

        if (inserted > 0 || updated > 0)
            await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "TTS catalogue sync complete: {Inserted} inserted, {Updated} updated ({Total} catalogue voices).",
            inserted,
            updated,
            byId.Count
        );
    }

    // Copies the provider's voice info onto the row, leaving IsDefault (and the natural-key Id) alone. Styles/tags
    // are stored as JSON arrays; a null/empty collection clears the column rather than storing "[]".
    private static void ApplyMetadata(TtsVoice row, TtsVoiceInfo info)
    {
        row.Name = info.Name;
        row.DisplayName = info.DisplayName;
        row.Locale = info.Locale;
        row.Gender = info.Gender;
        row.Provider = info.Provider;
        row.Accent = info.Accent;
        row.Age = info.Age;
        row.StylesJson = SerializeList(info.Styles);
        row.TagsJson = SerializeList(info.Tags);
        row.Description = info.Description;
        row.PreviewUrl = info.PreviewUrl;
    }

    private static string? SerializeList(IReadOnlyList<string>? values) =>
        values is { Count: > 0 } ? JsonConvert.SerializeObject(values) : null;
}
