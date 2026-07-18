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
/// Flags a personally-identifying event-payload property (dev-platform.md §1.2). The reflection emitter keeps
/// a <see cref="PiiAttribute"/> property in trusted contexts (broadcaster/script) but STRIPS it from the
/// public, viewer-facing context (widget) so untrusted browser code never receives it. The field still exists
/// on the C# record and server-side consumers; only the generated public surface omits it.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class PiiAttribute : Attribute;
