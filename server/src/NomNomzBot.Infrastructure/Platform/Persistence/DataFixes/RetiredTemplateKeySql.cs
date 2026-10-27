// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;

namespace NomNomzBot.Infrastructure.Platform.Persistence.DataFixes;

/// <summary>The SQL dialect a migration set runs on.</summary>
public enum TemplateSqlDialect
{
    Sqlite,
    Postgres,
}

/// <summary>
/// Builds the SQL that rewrites <c>{old</c> to <c>{new</c> in every <see cref="TemplateTextColumn"/>, from
/// <see cref="RetiredTemplateKeyMap"/>. Both migration sets call it, so the two stay identical.
/// <para>
/// The rewrite uses nested <c>REPLACE</c>, which both databases have. It matches a key only straight after
/// <c>{</c> and straight before <c>}</c> or <c>:</c>, so arguments stay intact and <c>{spaceship}</c> stays
/// as it is. An escaped brace (<c>\{</c>) is hidden behind a marker while the keys are rewritten, then put
/// back, so escaped text is never touched. <c>REPLACE</c> is case-sensitive, so each key is matched in its
/// lower-case, upper-case and capitalized spelling.
/// </para>
/// </summary>
public static class RetiredTemplateKeySql
{
    private const string EscapedBrace = "\\{";
    private const string EscapedBraceMarker = "@@nnz-escaped-brace@@";
    private static readonly char[] Terminators = ['}', ':'];

    /// <summary>One UPDATE statement per template column, for the pairs of <see cref="RetiredTemplateKeyMap"/>.</summary>
    public static IReadOnlyList<string> Statements(TemplateSqlDialect dialect) =>
        Statements(dialect, RetiredTemplateKeyMap.Pairs);

    /// <summary>One UPDATE statement per template column, for the given old key and new key pairs.</summary>
    public static IReadOnlyList<string> Statements(
        TemplateSqlDialect dialect,
        IReadOnlyList<KeyValuePair<string, string>> pairs
    ) => [.. TemplateTextColumn.All.Select(column => Statement(column, dialect, pairs))];

    /// <summary>The nested REPLACE expression that rewrites <paramref name="textExpression"/>.</summary>
    public static string RewriteExpression(
        string textExpression,
        IReadOnlyList<KeyValuePair<string, string>> pairs
    )
    {
        string expression = Replace(textExpression, EscapedBrace, EscapedBraceMarker);
        foreach (KeyValuePair<string, string> pair in pairs)
        {
            foreach (string oldKey in Spellings(pair.Key))
            {
                foreach (char terminator in Terminators)
                    expression = Replace(
                        expression,
                        "{" + oldKey + terminator,
                        "{" + pair.Value + terminator
                    );
            }
        }

        return Replace(expression, EscapedBraceMarker, EscapedBrace);
    }

    private static string Statement(
        TemplateTextColumn column,
        TemplateSqlDialect dialect,
        IReadOnlyList<KeyValuePair<string, string>> pairs
    )
    {
        string table = $"\"{column.Table}\"";
        string name = $"\"{column.Column}\"";

        if (dialect == TemplateSqlDialect.Postgres)
        {
            if (column.Shape == TemplateTextColumnShape.Jsonb)
                return $"UPDATE {table} SET {name} = ({RewriteExpression(name + "::text", pairs)})::jsonb "
                    + $"WHERE {RewriteExpression(name + "::text", pairs)} <> {name}::text;";

            if (column.Shape == TemplateTextColumnShape.TextArray)
                return $"UPDATE {table} SET {name} = ARRAY(SELECT {RewriteExpression("u.x", pairs)} "
                    + $"FROM unnest({name}) WITH ORDINALITY AS u(x, n) ORDER BY u.n) "
                    + $"WHERE EXISTS (SELECT 1 FROM unnest({name}) AS u(x) WHERE {RewriteExpression("u.x", pairs)} <> u.x);";
        }

        string rewritten = RewriteExpression(name, pairs);
        string lengthGuard =
            dialect == TemplateSqlDialect.Postgres && column.MaxLength is int max
                ? string.Create(CultureInfo.InvariantCulture, $" AND LENGTH({rewritten}) <= {max}")
                : string.Empty;
        return $"UPDATE {table} SET {name} = {rewritten} WHERE {rewritten} <> {name}{lengthGuard};";
    }

    private static string Replace(string expression, string find, string replacement) =>
        $"REPLACE({expression}, '{find}', '{replacement}')";

    private static IEnumerable<string> Spellings(string key) =>
        new[] { key, key.ToUpperInvariant(), char.ToUpperInvariant(key[0]) + key[1..] }.Distinct(
            StringComparer.Ordinal
        );
}
