// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Chat.Decoration;

/// <summary>
/// The permittable action keys behind the viewer-standing decoration steps. A live badge (subscriber and above)
/// still unlocks them, but so does a <c>!permit</c> capability grant or a resolved level that meets the action's
/// default — the standing is never gated by Twitch alone.
/// </summary>
public static class ChatDecorationCapabilities
{
    /// <summary>Have inline HTML in the sender's message rendered (on the <c>use_chat_html</c> feature).</summary>
    public const string RenderHtml = "chat:html:render";

    /// <summary>
    /// Keep a link in the sender's rendered HTML that points off the channel's link allowlist. Without it such a link
    /// is unwrapped to its text; an allowlisted host keeps its link for everyone who may render HTML.
    /// </summary>
    public const string LinkExternally = "chat:html:external-links";

    /// <summary>Have a link in the sender's message fetched for a preview card (on the <c>use_link_preview</c> feature).</summary>
    public const string PreviewLinks = "chat:link:preview";
}
