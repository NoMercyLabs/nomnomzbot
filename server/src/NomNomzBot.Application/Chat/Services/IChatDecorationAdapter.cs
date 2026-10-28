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
