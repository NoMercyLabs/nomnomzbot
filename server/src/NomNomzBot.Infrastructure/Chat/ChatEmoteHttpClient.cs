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

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// The named <see cref="HttpClient"/> the third-party emote/badge adapters fetch through
/// (chat-decoration spec §7). The targets are fixed public provider APIs (BTTV/FFZ/7TV), not user-supplied URLs,
/// so this is a plain resilient client — distinct from the SSRF-hardened egress client used for link previews.
/// The product User-Agent is applied globally (see <c>Platform/Http/AppUserAgent</c>), so this only pins a timeout.
/// </summary>
internal static class ChatEmoteHttpClient
{
    public const string Name = "chat-emote-providers";
}
