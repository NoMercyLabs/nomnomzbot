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

namespace NomNomzBot.Domain.Platform;

/// <summary>
/// Flags a personally-identifying event-payload property (dev-platform.md §1.2). The reflection emitter keeps
/// a <see cref="PiiAttribute"/> property in trusted contexts (broadcaster/script) but STRIPS it from the
/// public, viewer-facing context (widget) so untrusted browser code never receives it. The field still exists
/// on the C# record and server-side consumers; only the generated public surface omits it.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class PiiAttribute : Attribute;
