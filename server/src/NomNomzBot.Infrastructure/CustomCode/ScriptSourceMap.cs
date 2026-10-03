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
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Contracts.CustomCode;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// Reads the source map that esbuild appends to a script bundle and turns a position in the bundle back into the
/// author's file, line and column. The bundle drops blank lines and comments, so a bundle line is not a source line.
/// </summary>
public sealed class ScriptSourceMap
{
    private const string Marker = "//# sourceMappingURL=data:application/json;base64,";
    private const string Base64Digits =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    private readonly IReadOnlyList<string> _sources;
    private readonly List<List<Segment>> _lines;

    private ScriptSourceMap(IReadOnlyList<string> sources, List<List<Segment>> lines)
    {
        _sources = sources;
        _lines = lines;
    }

    private readonly record struct Segment(int GeneratedColumn, int Source, int Line, int Column);

    /// <summary>The map inside a bundle, or null when the bundle carries none or it cannot be read.</summary>
    public static ScriptSourceMap? FromBundle(string bundle)
    {
        int at = bundle.LastIndexOf(Marker, StringComparison.Ordinal);
        if (at < 0)
            return null;
        try
        {
            string base64 = bundle[(at + Marker.Length)..].Trim();
            JObject map = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
            List<string> sources = (map["sources"] ?? new JArray())
                .Select(s => (string?)s ?? "")
                .ToList();
            return new(sources, Decode(map.Value<string>("mappings") ?? ""));
        }
        catch (Exception ex) when (ex is FormatException or Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The author's position for a bundle position (line and column both from 1), or null when the map has nothing
    /// for that line.
    /// </summary>
    public ScriptSourcePosition? Map(int bundleLine, int bundleColumn)
    {
        if (bundleLine < 1 || bundleLine > _lines.Count)
            return null;
        List<Segment> line = _lines[bundleLine - 1];
        Segment? best = null;
        foreach (Segment segment in line)
        {
            if (segment.GeneratedColumn > bundleColumn - 1)
                break;
            best = segment;
        }
        best ??= line.Count > 0 ? line[0] : null;
        if (best is not { } found)
            return null;
        string? file =
            found.Source >= 0 && found.Source < _sources.Count ? _sources[found.Source] : null;
        return new(file, found.Line + 1, found.Column + 1);
    }

    private static List<List<Segment>> Decode(string mappings)
    {
        List<List<Segment>> lines = [];
        int source = 0;
        int line = 0;
        int column = 0;
        foreach (string generatedLine in mappings.Split(';'))
        {
            List<Segment> segments = [];
            int generatedColumn = 0;
            foreach (string text in generatedLine.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                List<int> fields = DecodeVlq(text);
                generatedColumn += fields[0];
                if (fields.Count < 4)
                    continue;
                source += fields[1];
                line += fields[2];
                column += fields[3];
                segments.Add(new(generatedColumn, source, line, column));
            }
            lines.Add(segments);
        }
        return lines;
    }

    private static List<int> DecodeVlq(string text)
    {
        List<int> values = [];
        int value = 0;
        int shift = 0;
        foreach (char c in text)
        {
            int digit = Base64Digits.IndexOf(c);
            if (digit < 0)
                throw new FormatException("Invalid source map digit.");
            value |= (digit & 31) << shift;
            if ((digit & 32) != 0)
            {
                shift += 5;
                continue;
            }
            values.Add((value & 1) == 1 ? -(value >> 1) : value >> 1);
            value = 0;
            shift = 0;
        }
        return values;
    }
}
