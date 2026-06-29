// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
