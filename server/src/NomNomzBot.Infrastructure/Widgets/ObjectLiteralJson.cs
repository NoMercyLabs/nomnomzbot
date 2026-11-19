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

using System.Globalization;
using System.Text;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Turns a JavaScript object literal in widget source into JSON text without running any of it. It accepts what a
/// static literal can hold: nested objects and arrays, strings in either quote (or a backtick string with no
/// <c>${}</c>), numbers, <c>true</c>, <c>false</c>, <c>null</c>, unquoted keys, comments and trailing commas. Anything
/// that needs evaluation (a name, a call, a spread, an operator) is a <see cref="Problem"/> at its source index.
/// </summary>
internal static class ObjectLiteralJson
{
    public readonly record struct Problem(int Index, string Message);

    /// <summary>Reads the literal whose opening brace is at <paramref name="start"/>.</summary>
    public static (string? Json, Problem? Problem) Convert(string source, int start)
    {
        StringBuilder json = new();
        int depth = 0;
        int i = start;
        while (i < source.Length)
        {
            char c = source[i];
            if (char.IsWhiteSpace(c))
            {
                json.Append(c);
                i++;
            }
            else if (c == '/' && Next(source, i) == '/')
            {
                i = EndOfLine(source, i);
            }
            else if (c == '/' && Next(source, i) == '*')
            {
                int close = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (close < 0)
                    return (null, new(i, "A comment inside the literal is never closed."));
                i = close + 2;
            }
            else if (c is '{' or '[')
            {
                depth++;
                json.Append(c);
                i++;
            }
            else if (c is '}' or ']')
            {
                DropTrailingComma(json);
                json.Append(c);
                i++;
                if (--depth == 0)
                    return (json.ToString(), null);
            }
            else if (c is ',' or ':')
            {
                json.Append(c);
                i++;
            }
            else
            {
                Problem? problem = AppendValue(source, ref i, json);
                if (problem is not null)
                    return (null, problem);
            }
        }

        return (null, new(start, "The object literal is never closed."));
    }

    private static Problem? AppendValue(string source, ref int i, StringBuilder json)
    {
        char c = source[i];
        if (c is '"' or '\'' or '`')
            return AppendString(source, ref i, json);
        if (char.IsAsciiDigit(c) || c == '-' || (c == '.' && char.IsAsciiDigit(Next(source, i))))
            return AppendNumber(source, ref i, json);
        if (char.IsAsciiLetter(c) || c is '_' or '$')
            return AppendWord(source, ref i, json);
        return new(i, $"'{c}' is not allowed in a static literal; use plain values only.");
    }

    private static Problem? AppendWord(string source, ref int i, StringBuilder json)
    {
        int start = i;
        while (
            i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] is '_' or '$')
        )
            i++;
        string word = source[start..i];

        int after = i;
        while (after < source.Length && char.IsWhiteSpace(source[after]))
            after++;
        if (after < source.Length && source[after] == ':')
        {
            json.Append('"').Append(word).Append('"');
            return null;
        }

        if (word is "true" or "false" or "null")
        {
            json.Append(word);
            return null;
        }

        return new(
            start,
            $"'{word}' is not a literal value; defineSettings needs plain values the server can read without running your code."
        );
    }

    private static Problem? AppendNumber(string source, ref int i, StringBuilder json)
    {
        int start = i;
        while (
            i < source.Length
            && (char.IsAsciiLetterOrDigit(source[i]) || source[i] is '.' or '-' or '+' or '_')
        )
            i++;
        string token = source[start..i];
        if (
            !double.TryParse(
                token,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double number
            ) || double.IsInfinity(number)
        )
            return new(start, $"'{token}' is not a plain decimal number.");

        json.Append(number.ToString("R", CultureInfo.InvariantCulture));
        return null;
    }

    private static Problem? AppendString(string source, ref int i, StringBuilder json)
    {
        int start = i;
        char quote = source[i++];
        json.Append('"');
        while (i < source.Length)
        {
            char c = source[i++];
            if (c == quote)
            {
                json.Append('"');
                return null;
            }

            if (quote == '`' && c == '$' && i < source.Length && source[i] == '{')
                return new(
                    start,
                    "A template string with ${} is not a literal; use a plain string."
                );

            Problem? problem =
                c == '\\' ? AppendEscape(source, ref i, json) : AppendPlain(c, quote, start, json);
            if (problem is not null)
                return problem;
        }

        return new(start, "A string inside the literal is never closed.");
    }

    private static Problem? AppendPlain(char c, char quote, int start, StringBuilder json)
    {
        switch (c)
        {
            case '"':
                json.Append("\\\"");
                return null;
            case '\n' when quote != '`':
                return new(start, "A string inside the literal is never closed.");
            case '\n':
                json.Append("\\n");
                return null;
            case '\r':
                json.Append("\\r");
                return null;
            case '\t':
                json.Append("\\t");
                return null;
            default:
                json.Append(c);
                return null;
        }
    }

    private static Problem? AppendEscape(string source, ref int i, StringBuilder json)
    {
        int at = i - 1;
        if (i >= source.Length)
            return new(at, "A string inside the literal is never closed.");

        char escaped = source[i++];
        switch (escaped)
        {
            case '\'' or '`':
                json.Append(escaped);
                return null;
            case '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't' or 'u':
                json.Append('\\').Append(escaped);
                return null;
            case '\n':
                return null;
            default:
                return new(at, $"The escape '\\{escaped}' is not supported in a static literal.");
        }
    }

    private static void DropTrailingComma(StringBuilder json)
    {
        int end = json.Length;
        while (end > 0 && char.IsWhiteSpace(json[end - 1]))
            end--;
        if (end > 0 && json[end - 1] == ',')
            json.Remove(end - 1, 1);
    }

    private static char Next(string source, int i) => i + 1 < source.Length ? source[i + 1] : '\0';

    private static int EndOfLine(string source, int i)
    {
        int newline = source.IndexOf('\n', i);
        return newline < 0 ? source.Length : newline;
    }
}
