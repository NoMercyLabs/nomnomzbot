// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// A surface a template string can be saved for. Determines which helper keys are valid — e.g. a
/// command trigger carries <c>{args.*}</c>, an event response never does (S042).
/// </summary>
public enum TemplateHelperContext
{
    /// <summary>Custom command trigger responses (chat-triggered, carries <c>{args.*}</c>).</summary>
    Command,

    /// <summary>Twitch EventSub-triggered chat responses (follow/sub/raid/etc.) — no command args.</summary>
    EventResponse,

    /// <summary>Timer rotation messages — no command args, no per-trigger user/target context.</summary>
    Timer,

    /// <summary>
    /// Pipeline action fields whose <see cref="PipelineActionFieldDescriptor.Templated"/> flag is set
    /// (S042b) — a pipeline can be bound to a chat-command trigger (carries <c>{args.*}</c>), an
    /// EventSub trigger, or a timer/manual trigger, so it gets the broadest helper set: everything a
    /// command or event-response template can use.
    /// </summary>
    Pipeline,
}
