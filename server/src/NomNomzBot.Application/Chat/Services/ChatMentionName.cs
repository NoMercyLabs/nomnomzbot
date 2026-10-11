// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// How a viewer is named in CHAT text. Twitch links and pings an @mention only by login, so a display
/// name that differs from the login by more than letter case (a non-Latin name) is written as the login.
/// Spoken renders (TTS) never use this: they keep the display name or the pronunciation override.
/// </summary>
public static class ChatMentionName
{
    /// <summary>Seed-variable key that tells the template resolver to render for chat, not for speech.</summary>
    public const string ChatRenderKey = "render.chat";

    public static string For(string displayName, string? login) =>
        string.IsNullOrWhiteSpace(login)
        || string.Equals(displayName, login, StringComparison.OrdinalIgnoreCase)
            ? displayName
            : login;

    /// <summary>
    /// The spoken form of a chat line: the login the chat line carries is put back to the display name,
    /// so TTS keeps saying what it said before (the display name, then the pronunciation override).
    /// </summary>
    public static string ToSpoken(string chatText, string displayName, string? login) =>
        string.Equals(For(displayName, login), displayName, StringComparison.Ordinal)
            ? chatText
            : Regex.Replace(
                chatText,
                $"(?<![A-Za-z0-9_/.]){Regex.Escape(login!)}(?![A-Za-z0-9_])",
                displayName.Replace("$", "$$", StringComparison.Ordinal),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            );

    /// <summary>The spoken form of a chat line for the triggering user held in a pipeline variable bag.</summary>
    public static string ToSpoken(string chatText, IReadOnlyDictionary<string, string> variables) =>
        variables.TryGetValue("user", out string? display)
        && variables.TryGetValue("user.name", out string? login)
            ? ToSpoken(chatText, display, login)
            : chatText;

    public static Dictionary<string, string> ForChat(IDictionary<string, string> variables)
    {
        Dictionary<string, string> copy = new(variables, StringComparer.OrdinalIgnoreCase)
        {
            [ChatRenderKey] = "true",
        };
        return copy;
    }

    /// <summary>True when the bag was marked by <see cref="ForChat"/>.</summary>
    public static bool IsChatRender(IReadOnlyDictionary<string, string> variables) =>
        variables.ContainsKey(ChatRenderKey);

    /// <summary>
    /// Rewrites the viewer-name variables of a chat render: the triggering user (login in
    /// <c>user.name</c>) and the target (login is the typed <c>target</c>).
    /// </summary>
    public static void ApplyToVariables(Dictionary<string, string> variables)
    {
        if (
            variables.TryGetValue("user", out string? user)
            && variables.TryGetValue("user.name", out string? userLogin)
        )
        {
            variables["user"] = For(user, userLogin);
            if (variables.ContainsKey("user.displayname"))
                variables["user.displayname"] = variables["user"];
        }

        if (!variables.TryGetValue("target", out string? typed) || string.IsNullOrWhiteSpace(typed))
            return;

        string targetLogin = typed.TrimStart('@');
        string? oldName = variables.GetValueOrDefault("target.name");
        foreach (string key in new[] { "target.displayname", "target.name" })
            if (variables.TryGetValue(key, out string? display))
                variables[key] = For(display, targetLogin);

        if (oldName is not null && variables.GetValueOrDefault("name") == oldName)
            variables["name"] = variables["target.name"];
    }
}
