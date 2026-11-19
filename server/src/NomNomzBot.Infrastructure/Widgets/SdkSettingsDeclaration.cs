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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Reads the settings form a widget declares from its own code with <c>nnz.defineSettings({ fields: [...] })</c>.
/// The server never runs overlay code: the first call in the project's files is read as a static object literal and
/// handed to <see cref="CustomWidgetSettingsDeclaration.Parse"/>, the same parser <c>settings.json</c> uses.
/// </summary>
internal static class SdkSettingsDeclaration
{
    public const string CallName = "defineSettings";

    /// <summary>Where a project calls <c>defineSettings</c>: the file, its text and the index of the call name.</summary>
    public readonly record struct Call(string Path, string Content, int NameIndex)
    {
        public int Line => LineOf(Content, NameIndex);
    }

    public static Call? Find(IReadOnlyDictionary<string, string> files)
    {
        foreach ((string path, string content) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (path == CustomWidgetSettingsDeclaration.FileName)
                continue;

            int index = FindCallName(content);
            if (index >= 0)
                return new(path, content, index);
        }

        return null;
    }

    public static Result<IReadOnlyList<WidgetSettingsField>> Read(Call call)
    {
        int open = call.Content.IndexOf('(', call.NameIndex + CallName.Length);
        int argument = open + 1;
        while (argument < call.Content.Length && char.IsWhiteSpace(call.Content[argument]))
            argument++;

        if (argument >= call.Content.Length || call.Content[argument] != '{')
            return Fail(
                call,
                argument,
                "defineSettings needs an object literal ({ fields: [...] }) written right in the call, not a variable or a call."
            );

        (string? json, ObjectLiteralJson.Problem? problem) = ObjectLiteralJson.Convert(
            call.Content,
            argument
        );
        if (problem is { } found)
            return Fail(call, found.Index, found.Message);

        Result<IReadOnlyList<WidgetSettingsField>> parsed = CustomWidgetSettingsDeclaration.Parse(
            json!
        );
        return parsed.IsFailure ? Fail(call, call.NameIndex, parsed.ErrorMessage!) : parsed;
    }

    private static Result<IReadOnlyList<WidgetSettingsField>> Fail(
        Call call,
        int index,
        string message
    )
    {
        int line = LineOf(call.Content, index);
        string located = $"{call.Path}:{line}: {message}";
        return Result.Failure<IReadOnlyList<WidgetSettingsField>>(
            located,
            CustomWidgetSettingsDeclaration.InvalidCode,
            errorData: new ProjectBuildFailure([
                new(
                    CustomWidgetSettingsDeclaration.InvalidCode,
                    message,
                    call.Path,
                    line,
                    ColumnOf(call.Content, index)
                ),
            ])
        );
    }

    // The first `defineSettings(` that is a call, not part of a longer name or a comment line.
    private static int FindCallName(string content)
    {
        int from = 0;
        while (from < content.Length)
        {
            int index = content.IndexOf(CallName, from, StringComparison.Ordinal);
            if (index < 0)
                return -1;

            from = index + CallName.Length;
            bool startsName = index == 0 || !IsNameChar(content[index - 1]);
            if (startsName && IsCallOpen(content, from) && !InComment(content, index))
                return index;
        }

        return -1;
    }

    private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '$';

    private static bool IsCallOpen(string content, int from)
    {
        int i = from;
        while (i < content.Length && char.IsWhiteSpace(content[i]))
            i++;
        return i < content.Length && content[i] == '(';
    }

    private static bool InComment(string content, int index)
    {
        int lineStart = content.LastIndexOf('\n', index) + 1;
        string before = content[lineStart..index];
        string trimmed = before.TrimStart();
        return before.Contains("//", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith('*');
    }

    private static int LineOf(string content, int index)
    {
        int line = 1;
        int limit = Math.Min(index, content.Length);
        for (int i = 0; i < limit; i++)
            if (content[i] == '\n')
                line++;
        return line;
    }

    private static int ColumnOf(string content, int index)
    {
        int clamped = Math.Min(index, content.Length);
        int lineStart = clamped == 0 ? 0 : content.LastIndexOf('\n', clamped - 1) + 1;
        return clamped - lineStart + 1;
    }
}
