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
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Tts.Entities;

namespace NomNomzBot.Infrastructure.Content.Tts;

/// <summary>
/// Seeds the shipped Edge-TTS voice catalogue (backend-structure §5.2, Order 10 — global
/// reference data, no FK dependencies). Idempotent: upserts by the voice's natural key
/// <see cref="TtsVoice.Id"/> (the Azure/Edge voice identifier), so a re-run updates the
/// editable fields of an existing voice and adds nothing new.
/// </summary>
public sealed class TtsVoiceSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;

    public TtsVoiceSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 10;

    private static readonly IReadOnlyList<TtsVoice> Voices =
    [
        new()
        {
            Id = "en-US-AriaNeural",
            Name = "AriaNeural",
            DisplayName = "Aria (US)",
            Locale = "en-US",
            Gender = "Female",
            Provider = "edge",
            IsDefault = true,
            Accent = "American",
        },
        new()
        {
            Id = "en-US-GuyNeural",
            Name = "GuyNeural",
            DisplayName = "Guy (US)",
            Locale = "en-US",
            Gender = "Male",
            Provider = "edge",
            IsDefault = false,
            Accent = "American",
        },
        new()
        {
            Id = "en-GB-SoniaNeural",
            Name = "SoniaNeural",
            DisplayName = "Sonia (GB)",
            Locale = "en-GB",
            Gender = "Female",
            Provider = "edge",
            IsDefault = false,
            Accent = "British",
        },
        new()
        {
            Id = "en-AU-NatashaNeural",
            Name = "NatashaNeural",
            DisplayName = "Natasha (AU)",
            Locale = "en-AU",
            Gender = "Female",
            Provider = "edge",
            IsDefault = false,
            Accent = "Australian",
        },
        new()
        {
            Id = "de-DE-KatjaNeural",
            Name = "KatjaNeural",
            DisplayName = "Katja (DE)",
            Locale = "de-DE",
            Gender = "Female",
            Provider = "edge",
            IsDefault = false,
            Accent = "German",
        },
        new()
        {
            Id = "fr-FR-DeniseNeural",
            Name = "DeniseNeural",
            DisplayName = "Denise (FR)",
            Locale = "fr-FR",
            Gender = "Female",
            Provider = "edge",
            IsDefault = false,
            Accent = "French",
        },
        new()
        {
            Id = "es-ES-ElviraNeural",
            Name = "ElviraNeural",
            DisplayName = "Elvira (ES)",
            Locale = "es-ES",
            Gender = "Female",
            Provider = "edge",
            IsDefault = false,
            Accent = "Castilian",
        },
        new()
        {
            Id = "ja-JP-NanamiNeural",
            Name = "NanamiNeural",
            DisplayName = "Nanami (JP)",
            Locale = "ja-JP",
            Gender = "Female",
            Provider = "edge",
            IsDefault = false,
            Accent = "Japanese",
        },
        new()
        {
            Id = "ko-KR-SunHiNeural",
            Name = "SunHiNeural",
            DisplayName = "Sun-Hi (KR)",
            Locale = "ko-KR",
            Gender = "Female",
            Provider = "edge",
            IsDefault = false,
            Accent = "Korean",
        },
        new()
        {
            Id = "pt-BR-FranciscaNeural",
            Name = "FranciscaNeural",
            DisplayName = "Francisca (BR)",
            Locale = "pt-BR",
            Gender = "Female",
            Provider = "edge",
            IsDefault = false,
            Accent = "Brazilian",
        },
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<string> existingIds = await _db.TtsVoices.Select(v => v.Id).ToListAsync(ct);
        HashSet<string> present = existingIds.ToHashSet(StringComparer.Ordinal);

        foreach (TtsVoice voice in Voices)
        {
            if (present.Contains(voice.Id))
                continue;

            _db.TtsVoices.Add(
                new()
                {
                    Id = voice.Id,
                    Name = voice.Name,
                    DisplayName = voice.DisplayName,
                    Locale = voice.Locale,
                    Gender = voice.Gender,
                    Provider = voice.Provider,
                    IsDefault = voice.IsDefault,
                    Accent = voice.Accent,
                }
            );
        }
    }
}
