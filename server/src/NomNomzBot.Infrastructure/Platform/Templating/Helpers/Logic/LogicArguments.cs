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

using System.Globalization;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Logic;

/// <summary>
/// One argument of a lazy helper split at its first structural separator (<c>:</c> for the value forms of
/// <c>if</c>, <c>=</c> for a <c>switch</c> case). <see cref="Tail"/> renders the part after the separator only
/// when asked, so a branch that is not chosen stays unrendered whenever the head is plain text.
/// </summary>
internal readonly record struct SplitArgument(string Head, Func<ValueTask<string>> Tail);

/// <summary>The small text rules every logic helper shares: truthiness, numbers and lazy argument splitting.</summary>
internal static class LogicArguments
{
    private const string Escapable = "{}|:\\";

    /// <summary>True for a non-blank text that is not <c>false</c>, <c>0</c> or <c>no</c> (catalog section 7.5).</summary>
    public static bool IsTrue(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)
        && !value.Trim().Equals("no", StringComparison.OrdinalIgnoreCase)
        && value.Trim() != "0";

    public static bool TryNumber(string text, out double number) =>
        double.TryParse(
            text.Trim().TrimEnd('%'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out number
        ) && double.IsFinite(number);

    /// <summary>Renders argument <c>index</c> of a lazy helper, trimmed; an argument that is never asked for never runs.</summary>
    public static async ValueTask<string> RenderAsync(
        HelperNode node,
        int index,
        CancellationToken cancellationToken
    ) =>
        (
            node.RenderArgumentAsync is { } render
                ? await render(index, cancellationToken)
                : node.Arguments[index]
        ).Trim();

    /// <summary>Renders argument <c>index</c>, or nothing when the helper has fewer arguments (an omitted else branch).</summary>
    public static async ValueTask<string> RenderOrEmptyAsync(
        HelperNode node,
        int index,
        CancellationToken cancellationToken
    ) => index < node.Arguments.Count ? await RenderAsync(node, index, cancellationToken) : "";

    public static bool IsPlainText(string raw) => !raw.Contains('{');

    /// <summary>The text with each escape (<c>\{ \} \| \: \\</c>) replaced by the character it prints.</summary>
    public static string Unescape(string raw)
    {
        System.Text.StringBuilder text = new(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length && Escapable.Contains(raw[i + 1]))
                i++;
            text.Append(raw[i]);
        }
        return text.ToString();
    }

    /// <summary>The index of the first <paramref name="separator"/> outside braces and escapes, or -1.</summary>
    public static int StructuralIndexOf(string raw, char separator)
    {
        int depth = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\')
                i++;
            else if (raw[i] == '{')
                depth++;
            else if (raw[i] == '}')
                depth--;
            else if (raw[i] == separator && depth == 0)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Splits argument <c>index</c> at its first structural <paramref name="separator"/>; null when it has none.
    /// A plain-text head is read from the source, so the tail renders only when it is asked for. A head with a
    /// token inside renders the whole argument once and cuts the rendered text at its first separator.
    /// </summary>
    public static async ValueTask<SplitArgument?> SplitAsync(
        HelperNode node,
        int index,
        char separator,
        CancellationToken cancellationToken
    )
    {
        string raw = node.Arguments[index];
        int split = StructuralIndexOf(raw, separator);
        if (split < 0)
            return null;

        string tailRaw = raw[(split + 1)..];
        if (!IsPlainText(raw[..split]))
            return await SplitRenderedAsync(node, index, separator, cancellationToken);

        string head = Unescape(raw[..split]);
        return new SplitArgument(
            head.Trim(),
            async () =>
                IsPlainText(tailRaw)
                    ? Unescape(tailRaw).Trim()
                    : (await RenderAsync(node, index, cancellationToken))[(head.Length + 1)..]
                        .Trim()
        );
    }

    private static async ValueTask<SplitArgument?> SplitRenderedAsync(
        HelperNode node,
        int index,
        char separator,
        CancellationToken cancellationToken
    )
    {
        string rendered = await RenderAsync(node, index, cancellationToken);
        int cut = rendered.IndexOf(separator);
        if (cut < 0)
            return null;

        string tail = rendered[(cut + 1)..].Trim();
        return new SplitArgument(rendered[..cut].Trim(), () => ValueTask.FromResult(tail));
    }
}
