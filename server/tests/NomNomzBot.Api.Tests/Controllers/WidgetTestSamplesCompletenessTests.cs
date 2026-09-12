// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// BUILD-TODO's "the event clicker doesn't reflect the actual widget" report: a widget's fire-bar button list
/// is driven by its REAL declared <c>EventSubscriptions</c> (fixed separately — S060-remaining), but the sample
/// PAYLOAD for a declared-yet-uncovered event type silently fell back to the generic <c>{ user }</c> placeholder.
/// For a handler that guards on a specific field (chat_box.vue's <c>onEnriched</c> returns without <c>title</c>,
/// <c>onMessageDeleted</c> without <c>messageId</c>, now_playing.vue's pulse without <c>isSaved</c>), that made a
/// real, genuinely-subscribed button a silent no-op — the click did nothing, which reads exactly like "the
/// rendered widget does not reflect the actual widget".
///
/// <para>
/// This is the completeness guard: every event type any first-party widget actually declares must have a
/// sample that is not the bare fallback, so a newly-declared subscription with no matching sample fails loudly
/// here instead of shipping a dead test button.
/// </para>
/// </summary>
public sealed class WidgetTestSamplesCompletenessTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string Shape(string eventType) =>
        JsonSerializer.Serialize(WidgetTestSamples.For(eventType), Json);

    [Fact]
    public void Every_event_type_declared_by_a_first_party_widget_has_a_real_sample()
    {
        string fallbackShape = Shape("__no_widget_declares_this_event_type__");

        List<string> declared = FirstPartyWidgetCatalogue
            .All.SelectMany(widget => widget.DefaultEventSubscriptions)
            .Distinct()
            .ToList();
        declared.Should().NotBeEmpty();

        List<string> stillFallingBackToTheGenericDefault = declared
            .Where(eventType => Shape(eventType) == fallbackShape)
            .ToList();

        stillFallingBackToTheGenericDefault
            .Should()
            .BeEmpty(
                "a widget that genuinely declares this event gets a real fire-bar/test button, and firing it "
                    + "with the bare {{ user }} placeholder silently no-ops any handler that reads a "
                    + "different field — exactly the 'event clicker doesn't reflect the actual widget' report"
            );
    }

    [Fact]
    public void ChatMessageEnriched_sample_carries_a_title_so_chat_box_onEnriched_does_not_drop_it()
    {
        // chat_box.vue: `function onEnriched(e) { if (!e.title) return }` — named explicitly because it is
        // the exact defect reported (song-request card button rendered nothing).
        JsonElement payload = JsonSerializer.SerializeToElement(
            WidgetTestSamples.For("ChatMessageEnriched"),
            Json
        );

        payload.GetProperty("title").GetString().Should().NotBeNullOrEmpty();
    }
}
