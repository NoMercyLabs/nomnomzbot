// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
