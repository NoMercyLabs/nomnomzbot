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
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.CustomCode;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Reads the settings form a widget declares from its own code with <c>nnz.defineSettings({ fields: [...] })</c>.
/// The server never runs overlay code: TypeScript has its types stripped by esbuild, Acornima parses the JavaScript,
/// the first call is found in the syntax tree, and its object literal is converted to JSON for
/// <see cref="CustomWidgetSettingsDeclaration.Parse"/>, the same parser <c>settings.json</c> uses. A call the reader
/// cannot read is a failure with the file and line, never a silent skip.
/// </summary>
internal static class SdkSettingsDeclaration
{
    public const string CallName = "defineSettings";

    /// <summary>The declaration a project's code makes: the file and line of the call and what reading it gave.</summary>
    public sealed record Declared(
        string Path,
        int Line,
        Result<IReadOnlyList<WidgetSettingsField>> Fields
    );

    // The call node and where its script came from, so a node position maps back to the author's line.
    private sealed record Call(string Path, CallExpression Node, ScriptSourceMap? Map);

    /// <summary>The declaration in the first file (path order) that calls <c>defineSettings</c>; null when none does.</summary>
    public static async Task<Declared?> ReadAsync(
        IReadOnlyDictionary<string, string> files,
        IWidgetBuildService build,
        CancellationToken cancellationToken
    )
    {
        foreach ((string path, string content) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (path == CustomWidgetSettingsDeclaration.FileName)
                continue;

            ProjectScriptSource.Script? script = content.Contains(
                CallName,
                StringComparison.Ordinal
            )
                ? ProjectScriptSource.Extract(path, content)
                : null;
            if (script is null)
                continue;

            Declared? found = await ReadFileAsync(path, script.Value, build, cancellationToken);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static async Task<Declared?> ReadFileAsync(
        string path,
        ProjectScriptSource.Script script,
        IWidgetBuildService build,
        CancellationToken cancellationToken
    )
    {
        string javaScript = script.Text;
        ScriptSourceMap? map = null;
        if (script.IsTypeScript)
        {
            Result<string> stripped = await build.StripTypesAsync(script.Text, cancellationToken);
            if (stripped.IsFailure)
                return StripFailure(path, stripped);

            javaScript = stripped.Value;
            map = ScriptSourceMap.FromBundle(javaScript);
        }

        try
        {
            CallExpression? node = FindCall(ParseScriptOrModule(javaScript));
            return node is null ? null : Read(new(path, node, map));
        }
        catch (ParseErrorException error)
        {
            return FailAt(path, Position(map, error.LineNumber, error.Column), error.Description);
        }
    }

    // Classic scripts first (inline <script>); a file with import/export only parses as a module.
    private static Node ParseScriptOrModule(string javaScript)
    {
        Parser parser = new();
        try
        {
            return parser.ParseScript(javaScript);
        }
        catch (ParseErrorException)
        {
            return parser.ParseModule(javaScript);
        }
    }

    private static Declared Read(Call call)
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
        return parsed.IsFailure
            ? Fail(call, call.Node, parsed.ErrorMessage!)
            : new(call.Path, Position(call.Map, call.Node).Line, parsed);
    }

    private static Declared Fail(Call call, Node at, string message) =>
        FailAt(call.Path, Position(call.Map, at), message);

    // esbuild reports its own errors against stdin; point them at the project file.
    private static Declared StripFailure(string path, Result<string> stripped)
    {
        ProjectBuildError? first = (
            stripped.ErrorData as ProjectBuildFailure
        )?.Errors.FirstOrDefault();
        return FailAt(
            path,
            new(null, first?.Line ?? 1, first?.Column ?? 1),
            first?.Message ?? stripped.ErrorMessage!
        );
    }

    private static Declared FailAt(string path, ScriptSourcePosition at, string message)
    {
        string located = $"{path}:{at.Line}: {message}";
        return new(
            path,
            at.Line,
            Result.Failure<IReadOnlyList<WidgetSettingsField>>(
                located,
                CustomWidgetSettingsDeclaration.InvalidCode,
                errorData: new ProjectBuildFailure([
                    new(
                        CustomWidgetSettingsDeclaration.InvalidCode,
                        message,
                        path,
                        at.Line,
                        at.Column
                    ),
                ])
            )
        );
    }

    private static ScriptSourcePosition Position(ScriptSourceMap? map, Node node) =>
        Position(map, node.Location.Start.Line, node.Location.Start.Column);

    // Acornima counts columns from 0; the source map and editors from 1.
    private static ScriptSourcePosition Position(ScriptSourceMap? map, int line, int column) =>
        map?.Map(line, column + 1) ?? new(null, line, column + 1);

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
