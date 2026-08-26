// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlinx.serialization.Serializable

/**
 * One counted category of a delete's blast radius (backend `BlastRadiusCategoryDto`). [categoryKey] is an
 * i18n resource KEY, never a sentence — the backend never ships English. [sample] carries up to a handful of
 * the dependents' names so the user recognises WHICH things break; a shorter sample than [count] means the
 * list was truncated, never that the count is soft.
 */
@Serializable
data class BlastRadiusCategory(
    val categoryKey: String = "",
    val count: Int = 0,
    val sample: List<String> = emptyList(),
)

/**
 * The real, backend-counted set of things that reference a resource right now (backend `BlastRadiusDto`).
 * [totalReferences] is zero exactly when nothing references it — the dialog renders that as an explicit
 * "nothing else references this" sentence, so the absence of dependents reads as verified-safe rather than
 * unknown. A lookup that FAILED never arrives as this type; it surfaces as [ApiResult.Failure] and gets its
 * own distinct message, because showing zero for a check that did not run causes the exact loss the preview
 * exists to prevent.
 *
 * [isMinimum] is set by the backend when some references can only be resolved at run time (a pipeline field
 * holding a template placeholder, or a custom code script that resolves the resource through the SDK). The
 * dialog must then say the number is a MINIMUM instead of implying completeness.
 */
@Serializable
data class BlastRadiusSummary(
    val categories: List<BlastRadiusCategory> = emptyList(),
    val isMinimum: Boolean = false,
) {
    val totalReferences: Int
        get() = categories.sumOf { it.count }
}
