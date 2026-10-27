// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chat;

/// <summary>The <c>chat</c> namespace (catalog section 7.14): limits, mentions, the trigger message, modes and the pinned line.</summary>
internal sealed class ChatTemplateHelpers : ChatNamespaceProvider
{
    public ChatTemplateHelpers(IServiceScopeFactory scopes)
        : this(scopes, new DbViewerDirectory(), new DbEmoteChatSource()) { }

    /// <summary>The second constructor lets a host (a test) register its own directory and chat source.</summary>
    public ChatTemplateHelpers(
        IServiceScopeFactory scopes,
        IViewerDirectory directory,
        IEmoteChatSource emoteChat
    )
        : base("chat", new ChatDataSource(scopes, directory, emoteChat)) { }
}

/// <summary>The <c>tts</c> namespace (catalog section 7.14): speech-ready text. No helper picks a voice.</summary>
internal sealed class TtsTemplateHelpers : ChatNamespaceProvider
{
    public TtsTemplateHelpers(IServiceScopeFactory scopes)
        : this(scopes, new DbViewerDirectory(), new DbEmoteChatSource()) { }

    /// <summary>The second constructor lets a host (a test) register its own directory and chat source.</summary>
    public TtsTemplateHelpers(
        IServiceScopeFactory scopes,
        IViewerDirectory directory,
        IEmoteChatSource emoteChat
    )
        : base("tts", new ChatDataSource(scopes, directory, emoteChat)) { }
}

/// <summary>The <c>link</c> namespace (catalog section 7.14): click-to-post links. Pure; it reads nothing.</summary>
internal sealed class LinkTemplateHelpers() : ChatNamespaceProvider("link", null);
