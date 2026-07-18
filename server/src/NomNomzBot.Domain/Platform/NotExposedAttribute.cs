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
/// Marks an event-payload property that must NEVER leave the server (dev-platform.md §1.2) — an internal
/// correlation id, a raw token, a routing handle. The reflection emitter stops at it: the property appears in
/// NO SDK context, in NO generated TS type, and in NO JSON schema, regardless of the event's tier.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class NotExposedAttribute : Attribute;
