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
/// <c>send_message</c>) the streamer can open in the editor and rearrange, never a hard-coded behaviour.
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
public sealed record CommandFlowSpec(
    string Command,
    string? Description,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Pools,
    IReadOnlyList<CommandFlowBranch> Branches
);

/// <summary>One rule of a <see cref="CommandFlowSpec"/>: when <paramref name="Condition"/> holds, answer from
/// <paramref name="Pool"/>. A null condition is the fallback and must be last.</summary>
public sealed record CommandFlowBranch(CommandFlowCondition? Condition, string Pool);

/// <summary>
/// A single comparison, expressed in the same terms the <c>comparison</c> condition block already understands
/// so an imported command is indistinguishable from one built by hand in the editor.
/// </summary>
/// <param name="Left">Left operand, usually a template such as <c>{args.1}</c> or <c>{target.messages}</c>.</param>
/// <param name="Operator">eq | ne | gt | lt | gte | lte | contains | starts_with | ends_with.</param>
/// <param name="Right">Right operand; may be empty (e.g. testing "no argument was given").</param>
public sealed record CommandFlowCondition(string Left, string Operator, string Right);
