// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using System.Text.RegularExpressions;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// Resolves a code-script project's relative <c>import</c> statements into ONE flat script the Jint sandbox can run
/// (S-OWN05: <c>import SCENES from './scenes'</c> threw at runtime because <see cref="CodeScriptService"/> only ever
/// compiled the manifest entry's own content — the rest of a multi-file project was stored but never read at
/// execution time). Jint has no ES module loader, so this is NOT a general bundler: it is a source-to-source rewrite
/// that (1) walks the relative import graph from the entry file to every depth (nested imports included), in
/// dependency-first order, failing closed on a missing file or a circular import; (2) wraps each imported module's
/// body in an IIFE that strips its <c>export</c> syntax and returns an object carrying its default + named exports;
/// and (3) rewrites every <c>import</c> line (entry and nested) into local <c>var</c> bindings read off that
/// object. The entry file's own top-level code is appended last, unwrapped, so its declarations become the same
/// Jint-global bindings a single-file script already produced — behavior for a project with no imports is
/// byte-for-byte unchanged.
///
/// Supported at this level: default / named / namespace imports and default / named exports (including
/// <c>export default &lt;expr&gt;</c>, <c>export const/let/var/function/class NAME</c>, and
/// <c>export { A, B as C }</c>) from sibling files anywhere in the relative import graph. NOT supported: bare-name
/// (package) imports (only <c>./</c> and <c>../</c> specifiers resolve — a bare import fails closed as unresolved,
/// same as an npm import already does), and TypeScript type-only constructs (<c>export type</c> /
/// <c>export interface</c>) — those were never valid Jint input before this resolver and remain a pre-existing,
/// orthogonal gap (Jint executes JS, not TS).
/// </summary>
public static class ScriptImportResolver
{
    private static readonly string[] ResolutionExtensions = [".ts", ".js", ".tsx", ".jsx"];

    private static readonly Regex ImportStatement = new(
        """^[ \t]*import\s+(?:(?<default>[A-Za-z_$][\w$]*)\s*,?\s*)?(?:\*\s*as\s+(?<ns>[A-Za-z_$][\w$]*)\s*)?(?:\{\s*(?<named>[^}]*)\}\s*)?from\s*(?<q>['"])(?<path>\.[^'"]+)\k<q>\s*;?[ \t]*\r?$""",
        RegexOptions.Multiline | RegexOptions.Compiled
    );

    private static readonly Regex NamedAlias = new(
        @"^([A-Za-z_$][\w$]*)\s+as\s+([A-Za-z_$][\w$]*)$",
        RegexOptions.Compiled
    );

    private static readonly Regex ExportDefault = new(
        @"export\s+default\s+",
        RegexOptions.Compiled
    );

    private static readonly Regex ExportDeclaration = new(
        @"^([ \t]*)export\s+(const|let|var|function\*?|class)\s+([A-Za-z_$][\w$]*)",
        RegexOptions.Multiline | RegexOptions.Compiled
    );

    private static readonly Regex ExportList = new(
        """^[ \t]*export\s*\{\s*([^}]*)\s*\}\s*;?[ \t]*\r?$""",
        RegexOptions.Multiline | RegexOptions.Compiled
    );

    private static readonly Regex NonIdentifierChars = new("[^A-Za-z0-9]", RegexOptions.Compiled);

    /// <summary>
    /// Produces the flat script Jint should compile/execute for this project. A project whose entry file has no
    /// relative imports (the pre-existing single-file case, and the common multi-file-but-unused-sibling case) comes
    /// back unchanged.
    /// </summary>
    public static Result<string> Resolve(
        IReadOnlyDictionary<string, string> files,
        string entryPath
    )
    {
        if (!files.TryGetValue(entryPath, out string? entrySource))
            return Result.Failure<string>(
                $"Entry file '{entryPath}' is not present in the project files.",
                "IMPORT_ENTRY_MISSING"
            );

        Dictionary<string, string> moduleIdByPath = new(StringComparer.Ordinal);
        List<string> emitOrder = [];
        Dictionary<string, string> emittedSnippets = new(StringComparer.Ordinal);
        HashSet<string> visiting = new(StringComparer.Ordinal);

        Result<string> Visit(string path)
        {
            if (moduleIdByPath.TryGetValue(path, out string? existingId))
                return Result.Success(existingId);
            if (!visiting.Add(path))
                return Result.Failure<string>(
                    $"Circular import detected involving '{path}'.",
                    "IMPORT_CYCLE"
                );

            if (!files.TryGetValue(path, out string? source))
                return Result.Failure<string>(
                    $"Imported file '{path}' is not present in the project files.",
                    "IMPORT_UNRESOLVED"
                );

            Result<string> processed = RewriteImports(path, source, files, Visit);
            if (processed.IsFailure)
                return Result.Failure<string>(processed.ErrorMessage!, processed.ErrorCode);

            string moduleId = "__mod_" + NonIdentifierChars.Replace(path, "_");
            moduleIdByPath[path] = moduleId;
            emitOrder.Add(path);
            emittedSnippets[path] = WrapModule(moduleId, processed.Value);
            visiting.Remove(path);
            return Result.Success(moduleId);
        }

        Result<string> entryProcessed = RewriteImports(entryPath, entrySource, files, Visit);
        if (entryProcessed.IsFailure)
            return Result.Failure<string>(entryProcessed.ErrorMessage!, entryProcessed.ErrorCode);

        if (emitOrder.Count == 0)
            return Result.Success(entrySource); // no imports reachable — identical to the pre-resolver script

        StringBuilder script = new();
        foreach (string path in emitOrder)
            script.Append(emittedSnippets[path]);
        script.Append(entryProcessed.Value);
        return Result.Success(script.ToString());
    }

    // Replaces every relative `import ... from './x'` line in `source` with local `var` bindings drawn off the
    // target module's exports object, resolving (and, via `visit`, recursively wrapping) the target first so its
    // module-id variable exists before this file references it.
    private static Result<string> RewriteImports(
        string sourcePath,
        string source,
        IReadOnlyDictionary<string, string> files,
        Func<string, Result<string>> visit
    )
    {
        string directory = GetDirectory(sourcePath);
        string? failure = null;
        string? failureCode = null;

        string transformed = ImportStatement.Replace(
            source,
            match =>
            {
                if (failure is not null)
                    return match.Value;

                string specifier = match.Groups["path"].Value;
                string candidateBase = CombineRelative(directory, specifier);
                string? targetPath = TryResolveFile(files, candidateBase);
                if (targetPath is null)
                {
                    failure =
                        $"Cannot resolve import '{specifier}' from '{sourcePath}' — no project file matches "
                        + $"'{candidateBase}' (tried exact, .ts, .js, .tsx, .jsx, /index.ts, /index.js).";
                    failureCode = "IMPORT_UNRESOLVED";
                    return match.Value;
                }

                Result<string> visited = visit(targetPath);
                if (visited.IsFailure)
                {
                    failure = visited.ErrorMessage;
                    failureCode = visited.ErrorCode;
                    return match.Value;
                }

                return BuildImportBindings(match, visited.Value);
            }
        );

        return failure is null
            ? Result.Success(transformed)
            : Result.Failure<string>(failure, failureCode ?? "IMPORT_UNRESOLVED");
    }

    private static string BuildImportBindings(Match match, string moduleId)
    {
        StringBuilder bindings = new();

        if (match.Groups["default"].Success)
            bindings
                .Append("var ")
                .Append(match.Groups["default"].Value)
                .Append(" = ")
                .Append(moduleId)
                .Append(".default;\n");

        if (match.Groups["ns"].Success)
            bindings
                .Append("var ")
                .Append(match.Groups["ns"].Value)
                .Append(" = ")
                .Append(moduleId)
                .Append(";\n");

        if (match.Groups["named"].Success)
            foreach (
                string rawPart in match
                    .Groups["named"]
                    .Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            )
            {
                string part = rawPart.Trim();
                if (part.Length == 0)
                    continue;

                Match alias = NamedAlias.Match(part);
                (string exported, string local) = alias.Success
                    ? (alias.Groups[1].Value, alias.Groups[2].Value)
                    : (part, part);
                bindings
                    .Append("var ")
                    .Append(local)
                    .Append(" = ")
                    .Append(moduleId)
                    .Append('.')
                    .Append(exported)
                    .Append(";\n");
            }

        return bindings.ToString();
    }

    // Strips this module's own `export` syntax (tracking what it declared) and wraps it in an IIFE returning an
    // object of its default + named exports, so the importer's generated `var` bindings above resolve against it.
    private static string WrapModule(string moduleId, string importsResolvedBody)
    {
        Dictionary<string, string> namedExports = new(StringComparer.Ordinal);

        string body = ExportDeclaration.Replace(
            importsResolvedBody,
            m =>
            {
                string name = m.Groups[3].Value;
                namedExports[name] = name;
                return m.Groups[1].Value + m.Groups[2].Value + " " + name;
            }
        );

        body = ExportList.Replace(
            body,
            m =>
            {
                foreach (
                    string rawPart in m.Groups[1]
                        .Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                )
                {
                    string part = rawPart.Trim();
                    if (part.Length == 0)
                        continue;
                    Match alias = NamedAlias.Match(part);
                    if (alias.Success)
                        namedExports[alias.Groups[2].Value] = alias.Groups[1].Value; // exported = local
                    else
                        namedExports[part] = part;
                }
                return string.Empty;
            }
        );

        bool hasDefault = ExportDefault.IsMatch(body);
        if (hasDefault)
            body = ExportDefault.Replace(body, "var __default = ", 1);

        StringBuilder module = new();
        module.Append("var ").Append(moduleId).Append(" = (function () {\n");
        module.Append(body).Append('\n');
        module.Append(
            "return { \"default\": (typeof __default !== 'undefined' ? __default : undefined)"
        );
        foreach ((string exported, string local) in namedExports)
            module.Append(", \"").Append(exported).Append("\": ").Append(local);
        module.Append(" };\n})();\n");
        return module.ToString();
    }

    private static string? TryResolveFile(
        IReadOnlyDictionary<string, string> files,
        string candidateBase
    )
    {
        if (files.ContainsKey(candidateBase))
            return candidateBase;
        foreach (string extension in ResolutionExtensions)
            if (files.ContainsKey(candidateBase + extension))
                return candidateBase + extension;
        foreach (string extension in ResolutionExtensions)
        {
            string indexed = candidateBase + "/index" + extension;
            if (files.ContainsKey(indexed))
                return indexed;
        }
        return null;
    }

    private static string CombineRelative(string directory, string specifier)
    {
        string combined = directory.Length == 0 ? specifier : directory + "/" + specifier;
        List<string> parts = [];
        foreach (string segment in combined.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
                continue;
            if (segment == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(segment);
        }
        return string.Join("/", parts);
    }

    private static string GetDirectory(string path)
    {
        int lastSlash = path.LastIndexOf('/');
        return lastSlash < 0 ? string.Empty : path[..lastSlash];
    }
}
