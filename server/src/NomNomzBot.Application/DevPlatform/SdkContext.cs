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

namespace NomNomzBot.Application.DevPlatform;

/// <summary>
/// Which SDK surface a generated <c>.d.ts</c> / event-catalog is emitted for (dev-platform.md §1.3, §3.1). The
/// context selects the visibility tier set an event must fall within to appear, and whether <c>[Pii]</c> fields
/// are kept:
/// <list type="bullet">
///   <item><see cref="Widget"/> — untrusted, viewer-facing browser code: <see cref="EventVisibility.Public"/>
///   tier ONLY, and <c>[Pii]</c> fields are stripped.</item>
///   <item><see cref="Script"/> — the channel owner's server-side sandboxed scripts:
///   <see cref="EventVisibility.Public"/> + <see cref="EventVisibility.Moderator"/> +
///   <see cref="EventVisibility.Broadcaster"/> tiers, and <c>[Pii]</c> fields are kept.</item>
/// </list>
/// <see cref="EventVisibility.Internal"/> is never emitted in either context.
/// </summary>
public enum SdkContext
{
    /// <summary>Untrusted, viewer-facing widget code — Public tier only, PII stripped.</summary>
    Widget = 0,

    /// <summary>The channel owner's sandboxed scripts — up to Broadcaster tier, PII kept.</summary>
    Script = 1,
}
