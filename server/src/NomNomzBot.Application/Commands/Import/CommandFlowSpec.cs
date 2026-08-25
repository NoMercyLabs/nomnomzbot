// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Commands.Import;

/// <summary>
/// A declarative description of one chat command's response behaviour: named pools of lines, and the ordered
/// rules that decide which pool answers. It is deliberately DATA, not code — the whole point is that the
/// resulting command is an ordinary pipeline of generic blocks (<c>if</c> → <c>pick_from_list</c> →
/// <c>send_message</c> → <c>play_tts</c>) the streamer can open in the editor and rearrange, never a
/// hard-coded behaviour.
/// <para>
/// The lines themselves are a channel's own content and never ship as product seed data.
/// </para>
/// </summary>
/// <param name="Command">Command name without the prefix, e.g. <c>hug</c>.</param>
/// <param name="Description">What the command does, shown in the dashboard.</param>
/// <param name="Pools">Pool name → its lines. Each becomes a channel pick list named <c>{command}.{pool}</c>.</param>
/// <param name="Branches">
/// Ordered rules, first match wins. Every rule but the last carries a <see cref="CommandFlowBranch.Condition"/>;
/// the last is the fallback and carries none.
/// </param>
/// <param name="Aliases">Extra trigger words answering the same flow (e.g. <c>hit</c> for <c>fight</c>).</param>
/// <param name="MinPermissionLevel">Ladder floor for the command; null keeps everyone.</param>
public sealed record CommandFlowSpec(
    string Command,
    string? Description,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Pools,
    IReadOnlyList<CommandFlowBranch> Branches,
    IReadOnlyList<string>? Aliases = null,
    int? MinPermissionLevel = null
);

/// <summary>One rule of a <see cref="CommandFlowSpec"/>: when <paramref name="Condition"/> holds, run
/// <paramref name="Answer"/>. A null condition is the fallback and must be last.</summary>
public sealed record CommandFlowBranch(CommandFlowCondition? Condition, CommandFlowAnswer Answer);

/// <summary>
/// What one branch actually says. Several commands build a line from more than one pool — an opener from one
/// list and the body from another — so an answer rolls each of its <paramref name="Picks"/> into its own
/// variable and then composes <paramref name="Message"/> from them. A single-pool answer is just one pick.
/// </summary>
/// <param name="Picks">Pools to roll, each into the named variable, before the message is composed.</param>
/// <param name="Message">The line to say, referencing the rolled variables as <c>{{name}}</c>.</param>
/// <param name="Speak">
/// True to also read the line aloud through TTS. This is not decoration: the commands being ported speak
/// every narrative line on stream, and a port that only prints them would be a quieter, different bot.
/// </param>
public sealed record CommandFlowAnswer(
    IReadOnlyList<CommandFlowPick> Picks,
    string Message,
    bool Speak
);

/// <summary>A single roll: draw one line from <paramref name="Pool"/> and keep it in <paramref name="Variable"/>.</summary>
public sealed record CommandFlowPick(string Pool, string Variable);

/// <summary>
/// A single comparison, expressed in the same terms the <c>comparison</c> condition block already understands
/// so an imported command is indistinguishable from one built by hand in the editor.
/// </summary>
/// <param name="Left">Left operand, usually a template such as <c>{args.1}</c> or <c>{target.messages}</c>.</param>
/// <param name="Operator">eq | ne | gt | lt | gte | lte | contains | starts_with | ends_with.</param>
/// <param name="Right">Right operand; may be empty (e.g. testing "no argument was given").</param>
public sealed record CommandFlowCondition(string Left, string Operator, string Right);
