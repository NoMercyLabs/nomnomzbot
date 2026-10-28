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
    /// <c>game</c>, <c>channel.name</c>, <c>channel.title</c>, <c>channel.game</c>, <c>channel.url</c>,
    /// <c>stream.url</c>, <c>channel.login</c>, <c>channel.avatar</c>, <c>stream.thumbnail</c>) supplied by the
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

    /// <summary>
    /// A viewer's own text (a reward input, spoken or shown) on a channel that enabled
    /// <c>use_viewer_templates</c>. It holds only helpers that are pure text or read-only channel and
    /// viewer info; a helper is in it only when its row lists <see cref="Viewer"/> by name. Nothing here
    /// writes state, includes another command, or reads custom data or another viewer's data.
    /// </summary>
    Viewer,
}
