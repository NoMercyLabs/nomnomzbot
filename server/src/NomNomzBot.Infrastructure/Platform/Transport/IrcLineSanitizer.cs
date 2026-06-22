// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Transport;

/// <summary>
/// Neutralises IRC line-control characters in outgoing chat text. An IRC line is terminated by CRLF, so a
/// message or reply-id containing CR, LF, or NUL — reachable from user-controlled chat arguments, usernames,
/// or template output — could otherwise inject additional IRC commands (e.g. a second <c>PRIVMSG</c> or a
/// moderation command). Centralised here so every send path on the IRC transport is sanitised identically.
/// </summary>
internal static class IrcLineSanitizer
{
    /// <summary>Twitch caps a chat message at 500 characters; longer text is truncated.</summary>
    public const int MaxMessageLength = 500;

    /// <summary>
    /// Replaces CR/LF/NUL with spaces (preserving the visible text) and truncates to the Twitch length cap.
    /// </summary>
    public static string Message(string message)
    {
        string cleaned = message.Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' ');
        return cleaned.Length > MaxMessageLength ? cleaned[..MaxMessageLength] : cleaned;
    }

    /// <summary>
    /// Strips characters that would break out of, or split, an IRCv3 message tag value (CR, LF, NUL, space,
    /// and the <c>;</c> tag separator). Legitimate Twitch message ids are UUIDs and are unaffected.
    /// </summary>
    public static string TagValue(string value)
    {
        Span<char> buffer =
            value.Length <= 128 ? stackalloc char[value.Length] : new char[value.Length];
        int length = 0;
        foreach (char c in value)
            if (c is not ('\r' or '\n' or '\0' or ' ' or ';'))
                buffer[length++] = c;
        return new string(buffer[..length]);
    }
}
