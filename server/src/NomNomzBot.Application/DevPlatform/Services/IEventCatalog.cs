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

namespace NomNomzBot.Application.DevPlatform.Services;

/// <summary>
/// The unified Event Catalog (dev-platform.md §1) — the single map of <c>domain event type → stable wire name →
/// visibility tier</c>, reflected from the C# event records at startup. The payload SHAPE is not stored here;
/// it is reflected on demand from <see cref="EventDescriptor.ClrType"/> by <see cref="ISdkTypeEmitter"/>, so the
/// records remain the one source of truth. Discovery fails fast on a duplicate wire name (a wiring bug, as with
/// the automation registry). This is ADDITIVE to the automation registry, which is untouched.
/// </summary>
public interface IEventCatalog
{
    /// <summary>Every discovered domain event, ordered by wire name (deterministic output).</summary>
    IReadOnlyList<EventDescriptor> Descriptors { get; }
}

/// <summary>
/// One discovered domain event on the catalog. Reflected from the record; the only manual inputs are the
/// optional <c>[Event]</c> / <c>[Pii]</c> / <c>[NotExposed]</c> attributes on the record itself.
/// </summary>
/// <param name="WireName">The stable dotted wire name — from <c>[Event("…")]</c> or the type-name convention.</param>
/// <param name="Visibility">The tier — from <c>[Event(tier)]</c> or the safe <see cref="EventVisibility.Broadcaster"/> default.</param>
/// <param name="ClrType">The CLR event record type; its public properties define the payload shape.</param>
public sealed record EventDescriptor(string WireName, EventVisibility Visibility, Type ClrType);
