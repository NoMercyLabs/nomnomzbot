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
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// Writes <c>interface NnzWidgetSettings</c> from a widget's settings schema, so <c>NomNomz.settings</c>,
/// <c>onSettings</c> and <c>WIDGET_SETTINGS</c> carry the real setting names and value types. A member's JSDoc is
/// the label and help text when the schema carries real text; a translation key alone helps no author, so a member
/// with only keys gets no JSDoc.
/// </summary>
internal static partial class WidgetSettingsTypeWriter
{
    public const string InterfaceName = "NnzWidgetSettings";

    [GeneratedRegex("^[A-Za-z_$][A-Za-z0-9_$]*$")]
    private static partial Regex Identifier();

    public static string Write(WidgetSettingsSchema schema)
    {
        StringBuilder sb = new();
        sb.AppendLine("/** The settings of this widget, from its settings schema. */");
        sb.AppendLine($"interface {InterfaceName} {{");
        foreach (WidgetSettingsField field in schema.Fields)
        {
            string? doc = Doc(field);
            if (doc is not null)
                sb.AppendLine($"  /** {doc} */");
            sb.AppendLine($"  {MemberName(field.Key)}: {TsType(field)};");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string MemberName(string key) => Identifier().IsMatch(key) ? key : Literal(key);

    private static string TsType(WidgetSettingsField field) =>
        field.Type switch
        {
            "bool" => "boolean",
            "number" => "number",
            "text" or "color" => "string",
            "select" => OptionUnion(field) ?? "string",
            "multiselect" => OptionUnion(field) is { } union ? $"({union})[]" : "string[]",
            _ => "unknown",
        };

    private static string? OptionUnion(WidgetSettingsField field) =>
        field.Options is { Count: > 0 } options
            ? string.Join(" | ", options.Select(option => Literal(option.Value)))
            : null;

    private static string Literal(string value) =>
        $"'{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}'";

    private static string? Doc(WidgetSettingsField field)
    {
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(field.Label.Text))
            parts.Add(field.Label.Text.Trim().TrimEnd('.'));
        if (!string.IsNullOrWhiteSpace(field.Help?.Text))
            parts.Add(field.Help.Text.Trim());
        if (parts.Count == 0)
            return null;

        string joined = string.Join(". ", parts);
        return joined.Replace("*/", "* /", StringComparison.Ordinal).ReplaceLineEndings(" ");
    }
}
