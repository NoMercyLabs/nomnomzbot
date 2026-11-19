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

using Acornima;
using Acornima.Ast;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Reads the settings form a widget declares from its own code with <c>nnz.defineSettings({ fields: [...] })</c>.
/// The server never runs overlay code: Acornima parses the project's JavaScript, the first call is found in the
/// syntax tree, and its object literal is converted to JSON for <see cref="CustomWidgetSettingsDeclaration.Parse"/>,
/// the same parser <c>settings.json</c> uses.
/// </summary>
internal static class SdkSettingsDeclaration
{
    public const string CallName = "defineSettings";

    /// <summary>Where a project calls <c>defineSettings</c>: the file and the call node.</summary>
    public readonly record struct Call(string Path, CallExpression Node)
    {
        public int Line => Node.Location.Start.Line;
    }

    public static Call? Find(IReadOnlyDictionary<string, string> files)
    {
        foreach ((string path, string content) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (path == CustomWidgetSettingsDeclaration.FileName)
                continue;

            CallExpression? node = FirstCall(path, content);
            if (node is not null)
                return new(path, node);
        }

        return null;
    }

    public static Result<IReadOnlyList<WidgetSettingsField>> Read(Call call)
    {
        if (
            call.Node.Arguments.Count == 0
            || call.Node.Arguments[0] is not ObjectExpression literal
        )
            return Fail(
                call,
                call.Node.Arguments.Count == 0 ? call.Node : call.Node.Arguments[0],
                "defineSettings needs an object literal ({ fields: [...] }) written right in the call, not a variable or a call."
            );

        (string? json, ObjectExpressionJson.Problem? problem) = ObjectExpressionJson.Convert(
            literal
        );
        if (problem is { } found)
            return Fail(call, found.Node, found.Message);

        Result<IReadOnlyList<WidgetSettingsField>> parsed = CustomWidgetSettingsDeclaration.Parse(
            json!
        );
        return parsed.IsFailure ? Fail(call, call.Node, parsed.ErrorMessage!) : parsed;
    }

    private static Result<IReadOnlyList<WidgetSettingsField>> Fail(
        Call call,
        Node at,
        string message
    )
    {
        Position start = at.Location.Start;
        string located = $"{call.Path}:{start.Line}: {message}";
        return Result.Failure<IReadOnlyList<WidgetSettingsField>>(
            located,
            CustomWidgetSettingsDeclaration.InvalidCode,
            errorData: new ProjectBuildFailure([
                new(
                    CustomWidgetSettingsDeclaration.InvalidCode,
                    message,
                    call.Path,
                    start.Line,
                    start.Column + 1
                ),
            ])
        );
    }

    // A file that is not JavaScript the parser reads (TypeScript, a syntax error) holds no declaration here; the
    // build reports its own syntax errors.
    private static CallExpression? FirstCall(string path, string content)
    {
        if (!content.Contains(CallName, StringComparison.Ordinal))
            return null;

        string? script = ProjectScriptSource.Extract(path, content);
        if (script is null)
            return null;

        Node? tree = ParseScriptOrModule(script);
        return tree is null ? null : FindCall(tree);
    }

    // Classic scripts first (inline <script>); a file with import/export only parses as a module.
    private static Node? ParseScriptOrModule(string script)
    {
        Parser parser = new();
        try
        {
            return parser.ParseScript(script);
        }
        catch (ParseErrorException)
        {
            try
            {
                return parser.ParseModule(script);
            }
            catch (ParseErrorException)
            {
                return null;
            }
        }
    }

    // Source order, depth first: the first `defineSettings(...)` or `x.defineSettings(...)` call.
    private static CallExpression? FindCall(Node node)
    {
        if (node is CallExpression call && IsDefineSettings(call.Callee))
            return call;

        foreach (Node child in node.ChildNodes)
        {
            CallExpression? found = FindCall(child);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static bool IsDefineSettings(Node callee) =>
        callee switch
        {
            Identifier identifier => identifier.Name == CallName,
            MemberExpression { Computed: false, Property: Identifier property } => property.Name
                == CallName,
            _ => false,
        };
}
