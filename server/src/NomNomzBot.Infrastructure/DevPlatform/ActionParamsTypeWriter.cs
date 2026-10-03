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
using NomNomzBot.Application.Abstractions.Pipeline;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// Types <c>actions.invoke</c> (script) and <c>NomNomz.actions.invoke</c> (widget): one <c>NnzActionParams</c>
/// member per registered <see cref="ICommandAction"/>, built from its <see cref="ICommandAction.Fields"/>, plus
/// the <c>NnzActionArgs</c> helper that makes <c>params</c> optional only for an action with no required field.
/// </summary>
internal static partial class ActionParamsTypeWriter
{
    public static string Write(IReadOnlyList<ICommandAction> actions)
    {
        StringBuilder sb = new();
        sb.AppendLine("/** The parameters of every pipeline action, keyed by action type. */");
        sb.AppendLine("interface NnzActionParams {");
        foreach (
            ICommandAction action in actions
                .GroupBy(a => a.ActionType, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(a => a.ActionType, StringComparer.Ordinal)
        )
        {
            sb.AppendLine($"  '{action.ActionType}': {{");
            foreach (PipelineActionFieldDescriptor field in action.Fields)
                sb.AppendLine(
                    $"    {MemberName(field.Name)}{(field.Required ? string.Empty : "?")}: {TsType(field)};"
                );
            sb.AppendLine("  };");
        }
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** The action types whose params object has no required field, so params may be left out. */"
        );
        sb.AppendLine("type NnzActionsWithOptionalParams = {");
        sb.AppendLine("  [K in keyof NnzActionParams]: {} extends NnzActionParams[K] ? K : never;");
        sb.AppendLine("}[keyof NnzActionParams];");
        return sb.ToString();
    }

    // Number and Boolean stay strict: ActionDefinition.GetInt/GetBool read a string as the default, and the
    // engine templates only Text fields, so a string there would pass the editor and be ignored at run time.
    private static string TsType(PipelineActionFieldDescriptor field)
    {
        string core = field.Kind switch
        {
            PipelineActionFieldKind.Number => "number",
            PipelineActionFieldKind.Boolean => "boolean",
            PipelineActionFieldKind.Enum when field.Options is { Count: > 0 } => string.Join(
                " | ",
                field.Options.Select(option => $"'{Escape(option)}'")
            ),
            PipelineActionFieldKind.KeyValueMap => "Record<string, string>",
            _ => "string",
        };

        if (!field.Repeatable)
            return core;
        return core.Contains(" | ", StringComparison.Ordinal) ? $"({core})[]" : $"{core}[]";
    }

    private static string MemberName(string name) =>
        Identifier().IsMatch(name) ? name : $"'{Escape(name)}'";

    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);

    [GeneratedRegex("^[A-Za-z_$][A-Za-z0-9_$]*$")]
    private static partial Regex Identifier();
}
