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

using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Jint;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the chat widgets show the bot's overlay-only line (<c>messageType = "overlay_line"</c>, the reply of a
/// command whose delivery is overlay only) even when hide-commands and hide-bots are on, and still drop an ordinary
/// command line or bot line. The real <c>isFilteredOut</c> (chat_box.vue) and <c>isCommandMessage</c> (the
/// chat_overlay bundle index.vue) are extracted verbatim (balanced-brace) from the shipped assets and run in Jint.
/// </summary>
public sealed class OverlayLineFilterTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string LoadChatBoxAsset()
    {
        Assembly assembly = typeof(FirstPartyWidgetCatalogueSeeder).Assembly;
        const string resourceName = "NomNomzBot.Infrastructure.Content.Widgets.Assets.chat_box.vue";
        using System.IO.Stream? stream = assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull($"the embedded asset '{resourceName}' should exist");
        using StreamReader reader = new(stream!);
        return reader.ReadToEnd();
    }

    private static string ExtractFunction(string source, string functionName)
    {
        Match start = Regex.Match(source, $@"function\s+{Regex.Escape(functionName)}\s*\(");
        start.Success.Should().BeTrue($"'{functionName}' must exist in the widget source");

        int braceOpen = source.IndexOf('{', source.IndexOf(')', start.Index));
        int depth = 0;
        int i = braceOpen;
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
        i.Should().BeLessThan(source.Length, $"'{functionName}' body must close its braces");
        return source[start.Index..(i + 1)];
    }

    // Strips the small set of TS annotations these helpers use so Jint (a plain ES engine) can parse them.
    private static string StripTypes(string text)
    {
        string noReturn = Regex.Replace(text, @"\)\s*:\s*(?:boolean|string)\s*\{", ") {");
        return Regex.Replace(
            noReturn,
            @"(?<=[\w\)])\s*:\s*(?:NnzDashboardChatMessageDto|Payload|boolean|string)(?=[,\)=\s])",
            ""
        );
    }

    // The shipped bot list, read from the asset so a renamed list fails here instead of passing on a stale copy.
    private static string KnownBotsLiteral(string source)
    {
        Match list = Regex.Match(source, @"const KNOWN_BOTS[^=]*=\s*(\[[^\]]*\])");
        list.Success.Should().BeTrue("chat_box.vue must declare KNOWN_BOTS");
        return list.Groups[1].Value;
    }

    private static bool ChatBoxFilters(bool hideCommands, bool hideBots, object message)
    {
        string source = LoadChatBoxAsset();
        Engine engine = new();
        engine.Execute(
            $"var cfg = {{ hideCommands: {(hideCommands ? "true" : "false")}, hideBots: {(hideBots ? "true" : "false")} }};"
        );
        engine.Execute($"var KNOWN_BOTS = {KnownBotsLiteral(source)};");
        engine.Execute(StripTypes(ExtractFunction(source, "isFilteredOut")));
        engine.SetValue("__messageJson", JsonSerializer.Serialize(message, CamelCase));
        return engine.Evaluate("isFilteredOut(JSON.parse(__messageJson))").AsBoolean();
    }

    private static bool ChatOverlayTreatsAsCommand(object message)
    {
        Dictionary<string, string> files = FirstPartyWidgetBundles.Read("chat_overlay");
        files.Should().ContainKey("index.vue");
        Engine engine = new();
        engine.Execute(StripTypes(ExtractFunction(files["index.vue"], "isCommandMessage")));
        engine.SetValue("__messageJson", JsonSerializer.Serialize(message, CamelCase));
        return engine.Evaluate("isCommandMessage(JSON.parse(__messageJson))").AsBoolean();
    }

    private static object OverlayLine(string text) =>
        new
        {
            Username = "nightbot",
            DisplayName = "Nightbot",
            Message = text,
            IsCommand = false,
            MessageType = "overlay_line",
            UserType = "bot",
        };

    private static object ViewerLine(string username, string text, bool isCommand = false) =>
        new
        {
            Username = username,
            DisplayName = username,
            Message = text,
            IsCommand = isCommand,
            MessageType = "text",
            UserType = "",
        };

    [Fact]
    public void Chat_box_shows_an_overlay_line_from_a_bot_name_while_hide_bots_is_on()
    {
        const string html = "<b>Choo choo</b><br><small>requested by Viewer</small>";

        bool hidden = ChatBoxFilters(hideCommands: true, hideBots: true, OverlayLine(html));

        hidden.Should().BeFalse("the bot's own overlay line is the answer a viewer asked for");
    }

    [Fact]
    public void Chat_box_shows_an_overlay_line_that_starts_with_a_bang_while_hide_commands_is_on()
    {
        bool hidden = ChatBoxFilters(
            hideCommands: true,
            hideBots: true,
            OverlayLine("!train is leaving platform 1")
        );

        hidden.Should().BeFalse();
    }

    [Fact]
    public void Chat_box_still_hides_a_known_bot_chat_line_when_hide_bots_is_on()
    {
        bool hidden = ChatBoxFilters(
            hideCommands: true,
            hideBots: true,
            ViewerLine("nightbot", "Follow us on socials")
        );

        hidden.Should().BeTrue();
    }

    [Fact]
    public void Chat_box_shows_a_known_bot_chat_line_when_hide_bots_is_off()
    {
        bool hidden = ChatBoxFilters(
            hideCommands: true,
            hideBots: false,
            ViewerLine("nightbot", "Follow us on socials")
        );

        hidden.Should().BeFalse();
    }

    [Theory]
    [InlineData("!sr some song", false)]
    [InlineData("!train", true)]
    public void Chat_box_still_hides_a_viewer_command_line_when_hide_commands_is_on(
        string text,
        bool flagged
    )
    {
        bool hidden = ChatBoxFilters(
            hideCommands: true,
            hideBots: true,
            ViewerLine("viewer", text, flagged)
        );

        hidden.Should().BeTrue();
    }

    [Fact]
    public void Chat_box_shows_a_normal_viewer_line()
    {
        bool hidden = ChatBoxFilters(
            hideCommands: true,
            hideBots: true,
            ViewerLine("viewer", "hello chat")
        );

        hidden.Should().BeFalse();
    }

    [Fact]
    public void Chat_overlay_keeps_an_overlay_line_that_starts_with_a_bang_while_hide_commands_is_on()
    {
        ChatOverlayTreatsAsCommand(OverlayLine("!train is leaving platform 1"))
            .Should()
            .BeFalse("hideCommands must not swallow the bot's own overlay line");
    }

    [Theory]
    [InlineData("!sr some song", false)]
    [InlineData("hello", true)]
    public void Chat_overlay_still_treats_a_viewer_command_as_a_command(string text, bool flagged)
    {
        ChatOverlayTreatsAsCommand(ViewerLine("viewer", text, flagged)).Should().BeTrue();
    }

    [Fact]
    public void Chat_overlay_does_not_treat_a_normal_viewer_line_as_a_command()
    {
        ChatOverlayTreatsAsCommand(ViewerLine("viewer", "hello chat")).Should().BeFalse();
    }
}
