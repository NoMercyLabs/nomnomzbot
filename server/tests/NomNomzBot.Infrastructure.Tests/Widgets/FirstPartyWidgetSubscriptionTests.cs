// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// A widget event reaches a widget only if that widget's catalogue entry DECLARES it — dispatch is
/// subscription-matched, so an event nobody declared is dropped before it is sent.
///
/// <para>
/// That makes the declaration and the widget's own <c>nnz.on(...)</c> calls two halves of one wiring, kept in
/// two different files and two different languages. Break either half and the feature dies in complete
/// silence: no error, no log, the widget just never updates. Found by mutation — removing
/// <c>ChatMessageEnriched</c> from the chat widgets' subscriptions left the whole suite green while the
/// song-request card would never have arrived.
/// </para>
/// </summary>
public sealed class FirstPartyWidgetSubscriptionTests
{
    // `nnz.on('some_event', handler)` — the widget-side half of the wiring.
    private static readonly Regex Subscribe = new(
        @"nnz\.on\(\s*['""]([^'""]+)['""]",
        RegexOptions.Compiled
    );

    private static string? ReadSource(string key)
    {
        string resourceName = $"NomNomzBot.Infrastructure.Content.Widgets.Assets.{key}.vue";
        using System.IO.Stream? stream = typeof(FirstPartyWidgetCatalogue)
            .GetTypeInfo()
            .Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Every_event_a_widget_listens_for_is_declared_in_its_catalogue_entry()
    {
        List<string> broken = [];

        foreach (FirstPartyWidgetDefinition widget in FirstPartyWidgetCatalogue.All)
        {
            string? source = ReadSource(widget.Key);
            if (source is null)
                continue; // Not every catalogue entry is backed by a bundled SFC.

            HashSet<string> listensFor =
            [
                .. Subscribe.Matches(source).Select(match => match.Groups[1].Value),
            ];
            HashSet<string> declared = [.. widget.DefaultEventSubscriptions];

            foreach (string undeclared in listensFor.Except(declared))
                broken.Add($"{widget.Key}.vue listens for '{undeclared}' but does not declare it");
        }

        broken
            .Should()
            .BeEmpty(
                "a widget that listens for an event it never declared is silently unreachable — dispatch "
                    + "drops the event before it is sent, and nothing anywhere reports it"
            );
    }

    [Fact]
    public void The_chat_widgets_declare_the_enrichment_that_carries_a_song_requests_track()
    {
        // Named explicitly, not just covered by the sweep above: this is the wiring the owner's "show the
        // Spotify/YouTube info instead of !sr <query>" depends on, and it has no other guard.
        IEnumerable<FirstPartyWidgetDefinition> chatWidgets = FirstPartyWidgetCatalogue.All.Where(
            w => w.DefaultEventSubscriptions.Contains("ChatMessage")
        );

        chatWidgets.Should().NotBeEmpty();
        foreach (FirstPartyWidgetDefinition widget in chatWidgets)
            widget
                .DefaultEventSubscriptions.Should()
                .Contain(
                    "ChatMessageEnriched",
                    "{0} renders chat lines, so it must receive the enrichment that replaces a song "
                        + "request's raw command text with the resolved track",
                    widget.Key
                );
    }
}
