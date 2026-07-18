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
/// Marks a domain-event record for the unified Event Catalog (dev-platform.md §1.2) and, optionally, overrides
/// its two catalog facets. Both arguments are optional — the common case is <b>zero annotation</b>: an
/// unadorned event is discovered by convention at the safe <see cref="EventVisibility.Broadcaster"/> default.
/// <para>
/// This attribute is metadata only — the ONE manual input, applied on the event record itself. The wire name,
/// TS type, JSON schema, and runtime projection are all reflected from the record; nothing else is authored.
/// </para>
/// </summary>
/// <param name="wireName">
/// The stable dotted wire name (e.g. <c>chat.message</c>). When null the catalog derives one from the type
/// name by convention (see <c>IEventCatalog</c>). Supply this to pin a name that must never drift.
/// </param>
/// <param name="visibility">The visibility tier; defaults to the safe <see cref="EventVisibility.Broadcaster"/>.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EventAttribute(
    string? wireName = null,
    EventVisibility visibility = EventVisibility.Broadcaster
) : Attribute
{
    /// <summary>The explicit wire name, or null to derive one by convention.</summary>
    public string? WireName { get; } = wireName;

    /// <summary>The visibility tier for this event.</summary>
    public EventVisibility Visibility { get; } = visibility;
}
