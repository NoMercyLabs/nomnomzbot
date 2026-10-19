// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Raids;

/// <summary>
/// One broken raid rule as data, so the dashboard phrases it in the owner's language.
/// </summary>
/// <param name="Reason">Stable reason key, one of the constants below.</param>
/// <param name="Field">Stable key of the rule group that broke (for example "category", "band", "followed").</param>
/// <param name="Subject">The name or label the owner gave the rule; empty for rules without one.</param>
/// <param name="Value">The offending number, when the reason has one.</param>
/// <param name="Limit">The limit the number broke, when the reason has one.</param>
public sealed record RaidRulesProblem(
    string Reason,
    string Field,
    string Subject,
    int? Value = null,
    int? Limit = null
)
{
    public const string WeightOutOfRange = "weight-out-of-range";
    public const string CategoryNoName = "category-no-name";
    public const string KeywordNoWords = "keyword-no-words";
    public const string KeywordEmptyWord = "keyword-empty-word";
    public const string KeywordMatchesNothing = "keyword-matches-nothing";
    public const string BandBelowZero = "band-below-zero";
    public const string BandMinAboveMax = "band-min-above-max";
    public const string StepNeedsDay = "step-needs-day";
}

/// <summary>
/// The failure data of a refused raid rule set: every broken rule. The API writes it into the 400
/// response <c>data</c> (code <c>VALIDATION_FAILED</c>).
/// </summary>
public sealed record RaidRulesProblems(IReadOnlyList<RaidRulesProblem> Problems)
    : IResponseErrorData;
