// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;

namespace NomNomzBot.Application.Commands.Dtos;

/// <summary>
/// One reply group of the built-in reply catalogue (commands-pipelines.md §11): the replies a built-in (or the
/// bot itself, for the <c>system</c>/<c>botstatus</c> groups) can send. <paramref name="CommandKeys"/> are the
/// chat triggers that speak with this group — e.g. <c>lurk</c> and <c>unlurk</c> share the <c>lurk</c> group; it
/// is empty for a group no command owns.
/// </summary>
public sealed record BuiltinReplyGroupDto(
    string BuiltinKey,
    IReadOnlyList<string> CommandKeys,
    IReadOnlyList<BuiltinReplyDto> Replies
);

/// <summary>
/// One reply slot as the channel sees it right now. <paramref name="EffectiveTemplate"/> is what the bot sends
/// (one pick among <paramref name="ToneVariations"/> when <paramref name="Source"/> is <c>tone</c>);
/// <paramref name="DefaultTemplate"/> is what it sends once the channel's own text is reset.
/// </summary>
public sealed record BuiltinReplyDto(
    string BuiltinKey,
    string Slot,
    LocalizedText Label,
    LocalizedText Description,
    string EffectiveTemplate,
    string Source,
    string DefaultTemplate,
    IReadOnlyList<string> ToneVariations,
    IReadOnlyList<BuiltinReplyVariableDto> Variables,
    bool IsOverridden,
    bool IsLocked
);

/// <summary>A variable a reply slot seeds, with the example value the dashboard preview fills in.</summary>
public sealed record BuiltinReplyVariableDto(
    string Name,
    LocalizedText Description,
    string SampleValue
);

/// <summary>Which layer of the precedence ladder a reply's effective text came from.</summary>
public static class BuiltinReplySource
{
    /// <summary>The channel's own text for this slot.</summary>
    public const string Channel = "channel";

    /// <summary>The platform admin's text for this slot.</summary>
    public const string Platform = "platform";

    /// <summary>The channel's personality tone lines.</summary>
    public const string Tone = "tone";
}
