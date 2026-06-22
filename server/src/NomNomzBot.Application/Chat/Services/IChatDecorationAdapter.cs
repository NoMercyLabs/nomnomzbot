// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Chat.Decoration;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// One pluggable step in the chat-message decoration pipeline (chat-decoration spec §0/§3.1). The orchestrator
/// (<c>IChatMessageDecorator</c>) discovers every adapter, runs them in ascending <see cref="Order"/>, and invokes
/// each whose <see cref="AppliesTo"/> gate passes — best-effort, mutating the shared <see cref="ChatDecorationContext"/>
/// in place. Adding a provider or a whole new enrichment concern is one new adapter class; the orchestrator is untouched.
/// </summary>
public interface IChatDecorationAdapter
{
    /// <summary>Pipeline position; the chain runs ascending. Built-ins leave gaps of 10 so steps can slot between.</summary>
    int Order { get; }

    /// <summary>Cheap gate — feature flag, viewer standing, or fragment content — deciding whether this step runs at all.</summary>
    bool AppliesTo(ChatDecorationContext context);

    /// <summary>Enriches the context in place. Best-effort: a throwing adapter is skipped and the message still emits.</summary>
    Task DecorateAsync(ChatDecorationContext context, CancellationToken ct = default);
}
