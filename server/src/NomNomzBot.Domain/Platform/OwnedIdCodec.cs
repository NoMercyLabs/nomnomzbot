// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform;

/// <summary>
/// The codec for owned identifiers, shared by every layer. Storage stays UUIDv7 <see cref="Guid"/>
/// everywhere; the API renders an owned id on the wire as its 26-char Crockford base32 ULID string
/// (<c>NomNomzBot.Api.Identifiers.GuidUlidCodec</c>, which delegates here). A pipeline step's free-form
/// <c>ConfigJson</c> can carry that same wire-form ULID for a resource-reference field (e.g. <c>run_code</c>'s
/// <c>code_script_id</c>) — this lives in Domain, not the Api project, so both
/// <c>CommandConfigValidator</c>/<c>PipelineService</c> (save-time normalization) and every
/// <c>ICommandAction</c> reading such a field at execution time (Infrastructure) can decode it without an
/// inward-only layering violation. A ULID and a UUIDv7 are both 128 bits, so <c>new Ulid(guid)</c> /
/// <c>ulid.ToGuid()</c> round-trips losslessly.
/// </summary>
public static class OwnedIdCodec
{
    /// <summary>Encodes an owned <see cref="Guid"/> as its 26-char ULID wire form.</summary>
    public static string Encode(Guid id) => new Ulid(id).ToString();

    /// <summary>
    /// Decodes an owned identifier, accepting a 26-char ULID string OR a raw <see cref="Guid"/> string. A ULID
    /// is tried first — its fixed 26-char length never collides with any <see cref="Guid"/> format (36
    /// hyphenated, 32 "N", 38 braced) — then a raw Guid. Returns <c>false</c> on null/empty/malformed input.
    /// </summary>
    public static bool TryDecode(string? value, out Guid id)
    {
        if (!string.IsNullOrEmpty(value))
        {
            if (Ulid.TryParse(value, out Ulid ulid))
            {
                id = ulid.ToGuid();
                return true;
            }

            if (Guid.TryParse(value, out Guid guid))
            {
                id = guid;
                return true;
            }
        }

        id = Guid.Empty;
        return false;
    }
}
