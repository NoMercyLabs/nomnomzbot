// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.ui

import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.pipelines_condition_help_compare_left
import nomnomzbot.composeapp.generated.resources.pipelines_condition_help_compare_operator
import nomnomzbot.composeapp.generated.resources.pipelines_condition_help_compare_right
import nomnomzbot.composeapp.generated.resources.pipelines_condition_help_min_role
import nomnomzbot.composeapp.generated.resources.pipelines_condition_help_percent
import nomnomzbot.composeapp.generated.resources.pipelines_condition_summary_comparison
import nomnomzbot.composeapp.generated.resources.pipelines_condition_summary_random
import nomnomzbot.composeapp.generated.resources.pipelines_condition_summary_user_role
import nomnomzbot.composeapp.generated.resources.pipelines_operator_contains
import nomnomzbot.composeapp.generated.resources.pipelines_operator_ends_with
import nomnomzbot.composeapp.generated.resources.pipelines_operator_eq
import nomnomzbot.composeapp.generated.resources.pipelines_operator_gt
import nomnomzbot.composeapp.generated.resources.pipelines_operator_gte
import nomnomzbot.composeapp.generated.resources.pipelines_operator_lt
import nomnomzbot.composeapp.generated.resources.pipelines_operator_lte
import nomnomzbot.composeapp.generated.resources.pipelines_operator_ne
import nomnomzbot.composeapp.generated.resources.pipelines_operator_starts_with
import org.jetbrains.compose.resources.StringResource

// Resource lookups for the condition explanations (S-PIPE-CONDITIONS-EXPLAINED). The catalogue carries only the
// resource-key suffixes; this is the one place that maps them to declared string resources. A key with no entry
// returns null, so a block or field without text simply shows none.

/** The one-line plain description of a condition block, by [BlockType.summaryKey]. */
internal fun conditionSummaryResource(summaryKey: String): StringResource? =
    when (summaryKey) {
        "comparison" -> Res.string.pipelines_condition_summary_comparison
        "random" -> Res.string.pipelines_condition_summary_random
        "user_role" -> Res.string.pipelines_condition_summary_user_role
        else -> null
    }

/** The one-line help text of a condition field, by [BlockField.helpKey]. */
internal fun conditionHelpResource(helpKey: String): StringResource? =
    when (helpKey) {
        "compare_left" -> Res.string.pipelines_condition_help_compare_left
        "compare_operator" -> Res.string.pipelines_condition_help_compare_operator
        "compare_right" -> Res.string.pipelines_condition_help_compare_right
        "percent" -> Res.string.pipelines_condition_help_percent
        "min_role" -> Res.string.pipelines_condition_help_min_role
        else -> null
    }

/** The plain label of a server comparison operator, or null for a stored value the picker does not know. */
internal fun operatorLabelResource(value: String): StringResource? =
    when (value) {
        "eq" -> Res.string.pipelines_operator_eq
        "ne" -> Res.string.pipelines_operator_ne
        "gt" -> Res.string.pipelines_operator_gt
        "lt" -> Res.string.pipelines_operator_lt
        "gte" -> Res.string.pipelines_operator_gte
        "lte" -> Res.string.pipelines_operator_lte
        "contains" -> Res.string.pipelines_operator_contains
        "starts_with" -> Res.string.pipelines_operator_starts_with
        "ends_with" -> Res.string.pipelines_operator_ends_with
        else -> null
    }
