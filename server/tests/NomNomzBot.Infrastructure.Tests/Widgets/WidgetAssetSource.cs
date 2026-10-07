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
/// Reads a shipped first-party widget and cuts real code blocks out of it, so a test can run the widget's own
/// logic in Jint instead of a hand-made copy.
/// </summary>
internal static class WidgetAssetSource
{
    /// <summary>
    /// A fake <c>window.setTimeout</c> clock for Jint. <c>advance(ms)</c> runs every timer due by then, in time
    /// order; <c>clock.pending</c> holds the timers still waiting.
    /// </summary>
    public const string FakeClock = """
        var clock = { now: 0, nextId: 1, pending: {} };
        var window = {
          setTimeout: function (fn, ms) { var id = clock.nextId++; clock.pending[id] = { at: clock.now + ms, fn: fn }; return id; },
          clearTimeout: function (id) { delete clock.pending[id]; },
        };
        var setTimeout = window.setTimeout;
        var clearTimeout = window.clearTimeout;
        function advance(ms) {
          var end = clock.now + ms;
          for (;;) {
            var dueId = null;
            for (var id in clock.pending) {
              if (clock.pending[id].at <= end && (dueId === null || clock.pending[id].at < clock.pending[dueId].at)) dueId = id;
            }
            if (dueId === null) break;
            var t = clock.pending[dueId];
            delete clock.pending[dueId];
            clock.now = t.at;
            t.fn();
          }
          clock.now = end;
        }
        """;

    public static string Load(string assetFileName)
    {
        Assembly assembly = typeof(FirstPartyWidgetCatalogueSeeder).Assembly;
        string resourceName = $"NomNomzBot.Infrastructure.Content.Widgets.Assets.{assetFileName}";
        using System.IO.Stream? stream = assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull($"the embedded asset '{resourceName}' should exist");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The balanced-brace block that opens at the first <c>{</c> after <paramref name="startPattern"/>.</summary>
    public static string ExtractBlock(string source, string startPattern)
    {
        Match start = Regex.Match(source, startPattern);
        start.Success.Should().BeTrue($"'{startPattern}' must exist in the widget source");

        int depth = 0;
        int i = source.IndexOf('{', start.Index);
        for (; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    break;
            }
        }
        i.Should().BeLessThan(source.Length, "the block must close its braces");
        return source[start.Index..(i + 1)];
    }

    /// <summary>The statements inside a <c>name(() =&gt; { ... })</c> callback, without the braces.</summary>
    public static string ExtractCallbackBody(string source, string callName)
    {
        string block = ExtractBlock(source, $@"{Regex.Escape(callName)}\(\(\)\s*=>\s*");
        return block[(block.IndexOf('{') + 1)..^1];
    }
}
