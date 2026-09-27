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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Tts.Services;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// See <see cref="IPlatformTtsVoiceDefault"/>. The default is the catalogue row flagged <c>IsDefault</c>
/// (seeded on Aria; the admin editor moves the flag). Read on every resolve, so an admin save takes effect on
/// the next utterance with no cache to flush; a duplicate flag (an imported catalogue) resolves deterministically.
/// </summary>
public sealed class PlatformTtsVoiceDefault(IApplicationDbContext db) : IPlatformTtsVoiceDefault
{
    public async Task<string?> GetVoiceIdAsync(CancellationToken ct = default) =>
        await db
            .TtsVoices.Where(v => v.IsDefault)
            .OrderBy(v => v.Id)
            .Select(v => v.Id)
            .FirstOrDefaultAsync(ct);
}
