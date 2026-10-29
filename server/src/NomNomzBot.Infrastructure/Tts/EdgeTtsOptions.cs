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

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// The values Microsoft's free read-aloud service checks, from the <c>Tts:Edge</c> settings. Microsoft moves
/// them now and then (most often the Chromium version it accepts), so a change is a setting and a restart.
/// A blank value keeps the shipped default. <see cref="EdgeClientIdentity"/> turns them into what is sent.
/// </summary>
public sealed class EdgeTtsOptions
{
    public const string SectionName = "Tts:Edge";

    /// <summary>The Chromium version Edge's own read-aloud client reports, as upstream edge-tts ships it.</summary>
    public const string DefaultChromiumVersion = "143.0.3650.75";

    /// <summary>The trusted client token of Edge's read-aloud extension.</summary>
    public const string DefaultTrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";

    /// <summary>Four dotted numbers, for example 143.0.3650.75. Feeds Sec-MS-GEC-Version and the User-Agent.</summary>
    public string? ChromiumVersion { get; set; }

    /// <summary>32 hex characters. Feeds the handshake URL, the Sec-MS-GEC token and the voice list.</summary>
    public string? TrustedClientToken { get; set; }
}
