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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Arguments;

/// <summary>One word of the command, changed: without its <c>@</c>, in capitals, its length, or URL-encoded.</summary>
internal sealed class ArgumentWordHelpers()
    : ArgumentHelperGroup(
        Word("args.<n>.clean", "args.clean", ".clean", "ViewerTwo", MentionParser.ParseUserMention),
        Word("args.<n>.upper", "args.upper", ".upper", "HELLO", w => w.ToUpperInvariant()),
        Word(
            "args.<n>.length",
            "args.length",
            ".length",
            "5",
            w => TextArguments.Elements(w).Count.ToString(CultureInfo.InvariantCulture)
        ),
        Word("args.<n>.urlEncoded", "args.urlEncoded", ".urlEncoded", "hello", Uri.EscapeDataString)
    )
{
    /// <summary>A row for <c>args.&lt;n&gt;&lt;suffix&gt;</c>; a position past the last word prints the token as written.</summary>
    private static ArgumentRow Word(
        string key,
        string helpId,
        string suffix,
        string sample,
        Func<string, string> change
    ) =>
        ArgumentRow.Shaped(
            key,
            helpId,
            sample,
            k => ArgumentKeys.IsPosition(k, suffix),
            call =>
                call.Arguments.Count == 0
                && ArgumentKeys.TryPosition(call.Node.Key, suffix, out int n)
                && call.Words.At(n) is { } word
                    ? change(word)
                    : null
        );
}
