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
using FluentAssertions;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The code editor is a served page (<c>Assets/editor</c>), so its markup and its scripts are two files with
/// no compiler between them: renaming an element id in the HTML, or wiring a control that was never added,
/// fails silently at runtime — the editor just stops reacting to a button. Nothing else in the build can see
/// that, so it is checked here, against the real source files the publish copies.
/// </summary>
public class EditorAssetContractTests
{
    private const string RelativeEditorPath = "src/NomNomzBot.Api/Assets/editor";

    private static readonly string EditorDirectory = Resolve();

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(EditorDirectory, fileName));

    [Fact]
    public void Every_element_the_scripts_look_up_exists_in_the_markup()
    {
        string markup = Read("index.html");
        string[] scripts = ["editor.js", "preview.js"];

        HashSet<string> declaredIds = Regex
            .Matches(markup, "id=\"(?<id>[^\"]+)\"")
            .Select(match => match.Groups["id"].Value)
            .ToHashSet(StringComparer.Ordinal);

        List<string> missing = [];
        foreach (string script in scripts)
        {
            foreach (
                Match match in Regex.Matches(Read(script), @"getElementById\('(?<id>[^']+)'\)")
            )
            {
                string id = match.Groups["id"].Value;
                if (!declaredIds.Contains(id))
                    missing.Add(
                        $"{script} looks up '{id}', which no element in index.html declares"
                    );
            }
        }

        missing.Should().BeEmpty();
    }

    [Fact]
    public void Every_asset_the_page_links_is_present()
    {
        string markup = Read("index.html");

        List<string> referenced =
        [
            .. Regex
                .Matches(markup, @"(?:href|src)=""(?<file>[^"":]+\.(?:css|js))""")
                .Select(match => match.Groups["file"].Value),
        ];

        referenced.Should().NotBeEmpty("the page must pull in its own stylesheet and module");
        foreach (string file in referenced)
            File.Exists(Path.Combine(EditorDirectory, file))
                .Should()
                .BeTrue($"index.html references '{file}'");
    }

    [Fact]
    public void The_preview_frame_stays_sandboxed_without_same_origin_access()
    {
        // The editor's CSP admits inline script SO THAT the generated preview document can run. That
        // relaxation is only contained while the frame has an opaque origin: adding allow-same-origin here
        // would hand user-authored widget code the editor's own origin, cookies and storage.
        string markup = Read("index.html");

        Match frame = Regex.Match(markup, "<iframe[^>]*id=\"previewFrame\"[^>]*>");

        frame.Success.Should().BeTrue("the preview frame must exist");
        frame.Value.Should().Contain("sandbox=\"allow-scripts\"");
        frame.Value.Should().NotContain("allow-same-origin");
    }

    [Fact]
    public void Every_label_id_the_page_uses_is_in_the_default_label_map()
    {
        Dictionary<string, string> defaults = ReadDefaultLabels();
        string markup = Read("index.html");
        string script = Read("editor.js");

        List<string> used = Regex
            .Matches(markup, @"data-i18n(?:-title|-aria|-placeholder)?=""(?<id>[^""]+)""")
            .Select(match => match.Groups["id"].Value)
            .Concat(
                Regex
                    .Matches(script, @"\bt\('(?<id>\w+)'")
                    .Select(match => match.Groups["id"].Value)
            )
            .Distinct(StringComparer.Ordinal)
            .ToList();

        used.Should().NotBeEmpty();
        used.Where(id => !defaults.ContainsKey(id))
            .Should()
            .BeEmpty("every label id a page element or t() call names must have a default");
    }

    [Fact]
    public void The_markup_fallback_text_equals_the_default_label()
    {
        Dictionary<string, string> defaults = ReadDefaultLabels();
        string markup = Read("index.html");

        List<string> mismatches = [];
        foreach (
            Match match in Regex.Matches(
                markup,
                @"<(?<tag>\w+)[^>]*\bdata-i18n=""(?<id>[^""]+)""[^>]*>(?<text>[^<]*)</\k<tag>>"
            )
        )
        {
            string id = match.Groups["id"].Value;
            if (!defaults.TryGetValue(id, out string? expected) || expected.Contains('{'))
                continue;
            string actual = System.Net.WebUtility.HtmlDecode(match.Groups["text"].Value).Trim();
            if (actual != expected)
                mismatches.Add($"{id}: html '{actual}' vs map '{expected}'");
        }

        mismatches.Should().BeEmpty();
    }

    [Fact]
    public void No_visible_text_in_the_markup_lacks_a_label_marker()
    {
        string markup = Read("index.html");
        markup = Regex.Replace(markup, "<!--.*?-->", "", RegexOptions.Singleline);
        markup = Regex.Replace(
            markup,
            @"<(script|style)\b.*?</\1>",
            "",
            RegexOptions.Singleline | RegexOptions.IgnoreCase
        );

        List<string> unmarked = [];
        foreach (
            Match match in Regex.Matches(markup, @"<(?<tag>\w+)(?<attrs>[^>]*)>(?<text>[^<]+)")
        )
        {
            string text = System.Net.WebUtility.HtmlDecode(match.Groups["text"].Value).Trim();
            if (!Regex.IsMatch(text, "[A-Za-z]"))
                continue;
            if (!match.Groups["attrs"].Value.Contains("data-i18n=", StringComparison.Ordinal))
                unmarked.Add($"<{match.Groups["tag"].Value}> '{text}'");
        }

        unmarked.Should().BeEmpty("every visible text must carry a data-i18n marker");
    }

    private static Dictionary<string, string> ReadDefaultLabels()
    {
        string script = Read("editor.js");
        const string start = "const DEFAULT_LABELS = Object.freeze({";
        int from = script.IndexOf(start, StringComparison.Ordinal);
        from.Should().BeGreaterThan(-1, "editor.js must declare DEFAULT_LABELS");
        int to = script.IndexOf("\n});", from, StringComparison.Ordinal);
        string block = script[(from + start.Length)..to];

        Dictionary<string, string> labels = new(StringComparer.Ordinal);
        foreach (
            Match match in Regex.Matches(
                block,
                @"^ {4}(?<id>\w+):\s*(?:'(?<s>(?:[^'\\]|\\.)*)'|""(?<d>(?:[^""\\]|\\.)*)"")",
                RegexOptions.Multiline
            )
        )
        {
            string value = match.Groups["s"].Success
                ? match.Groups["s"].Value
                : match.Groups["d"].Value;
            labels[match.Groups["id"].Value] = Regex.Unescape(value);
        }

        return labels;
    }

    private static string Resolve()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, RelativeEditorPath);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate '{RelativeEditorPath}' above '{AppContext.BaseDirectory}'."
        );
    }
}
