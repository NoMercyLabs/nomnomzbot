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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.CustomEvents.Events;

/// <summary>
/// Published each time a <see cref="Entities.CustomDataSource"/> successfully ingests a payload
/// (custom-events.md §3). Fires pipeline triggers on <c>custom.&lt;name&gt;</c> and seeds template
/// variables so overlays receive live values without requiring a DB read.
/// </summary>
public sealed class CustomDataReceivedEvent : DomainEventBase
{
    /// <summary>The slug that identifies the source (the <c>&lt;name&gt;</c> in <c>custom.&lt;name&gt;</c>).</summary>
    public required string SourceName { get; init; }

    /// <summary>
    /// Named fields extracted from the raw payload via the source's <c>FieldMapJson</c> map.
    /// Keys are field names (e.g. <c>bpm</c>); values are the extracted string representations.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Fields { get; init; }

    /// <summary>The raw JSON payload as received (capped at 64 KB). Used for pipeline variable access.</summary>
    public required string RawPayload { get; init; }
}
