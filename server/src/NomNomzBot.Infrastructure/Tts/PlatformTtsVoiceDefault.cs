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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Tts.Entities;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// See <see cref="IPlatformTtsVoiceDefault"/>. The default is the catalogue row flagged <c>IsDefault</c>
/// (seeded on Aria; the admin editor moves the flag). Read on every resolve, so an admin save takes effect on
/// the next utterance with no cache to flush; a duplicate flag (an imported catalogue) resolves deterministically.
/// </summary>
public sealed class PlatformTtsVoiceDefault(IApplicationDbContext db) : IPlatformTtsVoiceDefault
{
    public async Task<string> GetVoiceIdAsync(CancellationToken ct = default) =>
        await db
            .TtsVoices.Where(v => v.IsDefault)
            .OrderBy(v => v.Id)
            .Select(v => v.Id)
            .FirstOrDefaultAsync(ct)
        ?? TtsVoice.FallbackDefaultVoiceId;
}
