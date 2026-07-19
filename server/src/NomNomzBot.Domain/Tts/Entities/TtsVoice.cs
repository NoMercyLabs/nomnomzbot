// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Tts.Entities;

public class TtsVoice : BaseEntity
{
    [MaxLength(255)]
    public string Id { get; set; } = null!;

    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(255)]
    public string DisplayName { get; set; } = null!;

    [MaxLength(20)]
    public string Locale { get; set; } = null!;

    [MaxLength(10)]
    public string Gender { get; set; } = null!;

    [MaxLength(50)]
    public string Provider { get; set; } = null!;

    public bool IsDefault { get; set; }

    // ── Catalogue metadata (the ElevenLabs/Polly label model) — all nullable; powers search/filter and a
    // preview-before-pick UX. Populated from the seed (Edge) and the live provider sync (Azure/ElevenLabs);
    // metadata the adapters used to discard (ElevenLabs preview_url + labels) is now captured here.

    // Spoken accent, e.g. American / British / Australian (derived from locale for Edge, from labels otherwise).
    [MaxLength(50)]
    public string? Accent { get; set; }

    // Perceived age band, e.g. young / middle_aged / old — provider-supplied; null when unknown.
    [MaxLength(20)]
    public string? Age { get; set; }

    // JSON array of provider style/emotion names (e.g. ["cheerful","angry"]); null when the voice has none.
    public string? StylesJson { get; set; }

    // JSON array of searchable use-case labels (e.g. ["narration","gaming"]); null when none.
    public string? TagsJson { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    // Provider sample-audio url for preview-before-pick; null for Edge / self-synth voices.
    [MaxLength(2048)]
    public string? PreviewUrl { get; set; }
}
