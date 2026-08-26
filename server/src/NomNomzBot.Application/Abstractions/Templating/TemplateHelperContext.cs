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

    /// <summary>
    /// Discord notification rule templates (discord.md §3.2) — no command args, no per-trigger
    /// user/target context; carries the channel/stream/time/random/count/pick-list/custom-data/transform
    /// helpers plus a handful of Discord-specific seed aliases (<c>broadcaster</c>, <c>title</c>,
    /// <c>game</c>, <c>channel.name</c>, <c>channel.title</c>, <c>channel.game</c>) supplied by the
    /// trigger handlers, and <c>user.name</c>/<c>raw.message</c> when dispatched from a pipeline action.
    /// </summary>
    Discord,

    /// <summary>
    /// Outbound webhook body templates (webhooks.md §3.5) — a webhook subscribes to the same catalogue
    /// events a command/event-response/pipeline can fire from, and the delivered payload has no single
    /// closed shape the way Command/Discord/Timer do, so it validates against the BROADEST non-args
    /// helper set: everything an event-response template can use (channel/stream/time/random/count/
    /// pick-list/custom-data/transform, plus per-trigger user/target/pronoun helpers when the firing
    /// event carries one) minus <c>args.*</c>, which only exists for a live chat-command invocation.
    /// </summary>
    Webhook,
}
