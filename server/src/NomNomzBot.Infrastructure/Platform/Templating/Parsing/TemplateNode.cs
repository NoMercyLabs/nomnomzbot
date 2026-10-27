// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Parsing;

/// <summary>One node of a parsed template. <see cref="Raw"/> is the exact source text the node came from.</summary>
public abstract record TemplateNode(string Raw);

/// <summary>Plain text. <see cref="Text"/> has every escape applied; <see cref="TemplateNode.Raw"/> keeps them.</summary>
public sealed record TextNode(string Text, string Raw) : TemplateNode(Raw);

/// <summary>
/// One <c>{key}</c> or <c>{key:arg|arg}</c> token. The key and each argument are node lists, because a
/// helper may sit inside either. <see cref="Arguments"/> is null when the token has no <c>:</c>.
/// </summary>
public sealed record PlaceholderNode(
    IReadOnlyList<TemplateNode> Key,
    IReadOnlyList<IReadOnlyList<TemplateNode>>? Arguments,
    string Raw
) : TemplateNode(Raw)
{
    /// <summary>True when no helper sits in the key or an argument: the key is known before any render.</summary>
    public bool IsStatic =>
        Key.All(n => n is TextNode) && (Arguments?.All(a => a.All(n => n is TextNode)) ?? true);

    /// <summary>The whole token content with escapes applied (only meaningful when <see cref="IsStatic"/>).</summary>
    public string StaticContent =>
        TextOf(Key)
        + (Arguments is null ? string.Empty : ":" + string.Join("|", Arguments.Select(TextOf)));

    /// <summary>The text of a node list with no token in it.</summary>
    public static string TextOf(IReadOnlyList<TemplateNode> nodes) =>
        string.Concat(nodes.OfType<TextNode>().Select(t => t.Text));
}
