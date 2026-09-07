// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using DomainCodec = NomNomzBot.Domain.Platform.OwnedIdCodec;

namespace NomNomzBot.Api.Identifiers;

/// <summary>
/// The API-boundary codec for owned identifiers. Storage stays UUIDv7 <see cref="Guid"/> everywhere — nothing in
/// EF, events, SignalR handlers, or services changes; this is purely a wire encoding. On the way out an owned id is
/// rendered as its 26-char Crockford base32 ULID string; on the way in a wire id is decoded, accepting BOTH a ULID
/// string AND a raw <see cref="Guid"/> string (inbound tolerance, so existing clients/tests keep working). A thin
/// wrapper over <see cref="DomainCodec"/> — the actual codec lives in Domain so Infrastructure-layer pipeline
/// actions and <c>CommandConfigValidator</c>/<c>PipelineService</c> can decode a ConfigJson resource-id field too,
/// without an inward-only layering violation; this type is kept as the Api-layer name every existing controller,
/// model binder, and JSON converter already calls.
/// </summary>
public static class GuidUlidCodec
{
    /// <summary>Encodes an owned <see cref="Guid"/> as its 26-char ULID wire form.</summary>
    public static string Encode(Guid id) => DomainCodec.Encode(id);

    /// <summary>
    /// Decodes a wire identifier, accepting a 26-char ULID string OR a raw <see cref="Guid"/> string. A ULID is
    /// tried first — its fixed 26-char length never collides with any <see cref="Guid"/> format (36 hyphenated,
    /// 32 "N", 38 braced) — then a raw Guid. Returns <c>false</c> on null/empty/malformed input.
    /// </summary>
    public static bool TryDecode(string? value, out Guid id) =>
        DomainCodec.TryDecode(value, out id);
}
