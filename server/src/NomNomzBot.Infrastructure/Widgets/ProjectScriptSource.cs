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

using System.Text.RegularExpressions;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Turns a project file into JavaScript text a parser can read. A <c>.js</c> file is already JavaScript. An
/// <c>.html</c> or <c>.vue</c> file keeps only its <c>&lt;script&gt;</c> bodies; everything else becomes spaces, with
/// every line break kept, so a node's line and column in the result are the line and column in the file.
/// </summary>
internal static partial class ProjectScriptSource
{
    [GeneratedRegex(
        @"<script\b[^>]*>(?<body>.*?)</script>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline
    )]
    private static partial Regex ScriptBlock();

    [GeneratedRegex(@"<script\b[^>]*\blang\s*=\s*[""']tsx?[""']", RegexOptions.IgnoreCase)]
    private static partial Regex TypeScriptBlock();

    /// <summary>The script text of a file and whether it is TypeScript (types still to strip) rather than JavaScript.</summary>
    public readonly record struct Script(string Text, bool IsTypeScript);

    /// <summary>The script text of the file, or null when the file type holds no script the server reads.</summary>
    public static Script? Extract(string path, string content)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".js" or ".mjs" => new(content, false),
            ".ts" or ".mts" => new(content, true),
            ".html" or ".htm" or ".vue" => new(
                KeepScriptBodies(content),
                TypeScriptBlock().IsMatch(content)
            ),
            _ => null,
        };
    }

    private static string KeepScriptBodies(string content)
    {
        char[] masked = new char[content.Length];
        for (int i = 0; i < content.Length; i++)
            masked[i] = content[i] is '\n' or '\r' ? content[i] : ' ';

        foreach (Match block in ScriptBlock().Matches(content))
        {
            Group body = block.Groups["body"];
            content.CopyTo(body.Index, masked, body.Index, body.Length);
        }

        return new(masked);
    }
}
