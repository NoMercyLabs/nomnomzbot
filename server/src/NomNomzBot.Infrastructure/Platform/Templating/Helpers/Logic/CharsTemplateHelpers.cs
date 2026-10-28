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

using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Logic;

/// <summary>
/// The characters a template cannot type easily (catalog section 7.5): <c>{chars.nothing}</c>,
/// <c>{chars.newline}</c>, <c>{chars.space}</c>, <c>{chars.pipe}</c> and <c>{chars.zws}</c> (a zero-width
/// space that stops a mention).
/// </summary>
public sealed class CharsTemplateHelpers : LogicHelperProvider
{
    private const int ZeroWidthSpace = 0x200B;
    private const string KeyPrefix = "chars.";

    private static readonly Dictionary<string, string> Characters = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["nothing"] = "",
        ["newline"] = "\n",
        ["space"] = " ",
        ["pipe"] = "|",
        ["zws"] = char.ConvertFromUtf32(ZeroWidthSpace),
    };

    public override string Namespace => "chars";

    public override IReadOnlyList<TemplateHelperEntry> Entries { get; } =
    [.. Characters.Select(c => Row(KeyPrefix + c.Key, c.Value))];

    public override ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        string name = node.Key.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase)
            ? node.Key[KeyPrefix.Length..]
            : node.Key;
        return Result(
            node.Arguments.Count == 0 && Characters.TryGetValue(name, out string? text)
                ? text
                : null
        );
    }
}
