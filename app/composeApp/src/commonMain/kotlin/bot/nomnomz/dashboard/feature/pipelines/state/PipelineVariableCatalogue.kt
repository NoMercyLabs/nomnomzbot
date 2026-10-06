// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.state

import androidx.compose.ui.text.TextRange
import androidx.compose.ui.text.input.TextFieldValue
import bot.nomnomz.dashboard.core.network.BlockField
import bot.nomnomz.dashboard.core.network.PipelineStep
import bot.nomnomz.dashboard.core.network.RuntimePalette
import bot.nomnomz.dashboard.core.network.TemplateHelperDto

// The pure logic behind the pipeline template field's `{` variable picker: which variables exist at a step,
// how the typed `{fragment` is read, and where a picked token lands. No Compose UI here, so it is unit-tested
// directly.

/** A variable an earlier step writes for later steps, with the value it was given (a sample) when known. */
data class DeclaredVariable(val name: String, val sample: String)

/**
 * One row of the picker: the placeholder [key] (without braces), a [descriptionKey] to resolve (null for a
 * variable declared by an earlier step, which has no registry text), and a plain [sample] value.
 */
data class VariableOption(val key: String, val descriptionKey: String?, val sample: String)

/**
 * Variables declared by the steps BEFORE position [index] in [steps] (flat order), in declaration order and
 * without repeats. A field declares a variable when the [palette] marks it so; its value is the name, or the
 * field's declared default when the value is blank (a nameless field with no default declares nothing). The
 * sample is the step's own `value` param when it has one.
 */
fun declaredVariablesBefore(steps: List<PipelineStep>, index: Int, palette: RuntimePalette): List<DeclaredVariable> {
    val declared: MutableList<DeclaredVariable> = mutableListOf()
    for (step: PipelineStep in steps.take(index.coerceIn(0, steps.size))) {
        val fields: List<BlockField> = palette.action(step.action.type)?.fields ?: continue
        for (field: BlockField in fields.filter { it.declaresVariable }) {
            val name: String =
                step.action.params[field.key].orEmpty().trim().ifBlank { field.declaredVariableDefault.orEmpty() }
            if (name.isBlank() || declared.any { it.name == name }) continue
            declared += DeclaredVariable(name = name, sample = step.action.params["value"].orEmpty())
        }
    }
    return declared
}

/**
 * A spot in the pipeline tree: a lane ([parentStepId] + [branch]; both null for the root) and, when
 * [beforeStepId] names a step of that lane, the position in front of it (null means the end of the lane).
 */
data class VariableScope(val parentStepId: String? = null, val branch: String? = null, val beforeStepId: String? = null)

/**
 * The steps whose variables are visible at a spot in the pipeline tree, outermost first. The result holds the steps before the spot in its
 * own lane, then, for each enclosing block up the tree, the steps before that block in the block's own lane.
 * Nothing that runs after the spot is included.
 */
fun stepsInScope(steps: List<PipelineStep>, scope: VariableScope): List<PipelineStep> {
    fun lane(parent: String?, laneBranch: String?): List<PipelineStep> =
        steps.filter { it.parentStepId == parent && it.branch == laneBranch }.sortedBy { it.order ?: 0 }

    val chunks: MutableList<List<PipelineStep>> = mutableListOf()
    var parent: String? = scope.parentStepId
    var laneBranch: String? = scope.branch
    var stopAt: String? = scope.beforeStepId
    val seen: MutableSet<String> = mutableSetOf()
    while (true) {
        val laneSteps: List<PipelineStep> = lane(parent, laneBranch)
        val cut: Int = stopAt?.let { id -> laneSteps.indexOfFirst { it.id == id } }?.takeIf { it >= 0 } ?: laneSteps.size
        chunks += laneSteps.take(cut)
        val enclosing: PipelineStep = parent?.takeIf { seen.add(it) }?.let { id -> steps.firstOrNull { it.id == id } } ?: break
        stopAt = enclosing.id
        parent = enclosing.parentStepId
        laneBranch = enclosing.branch
    }
    return chunks.asReversed().flatten()
}

/** The variables declared by the steps that run before a spot in the pipeline tree; see [stepsInScope]. */
fun declaredVariablesInScope(steps: List<PipelineStep>, scope: VariableScope, palette: RuntimePalette): List<DeclaredVariable> {
    val scoped: List<PipelineStep> = stepsInScope(steps, scope)
    return declaredVariablesBefore(scoped, scoped.size, palette)
}

/** The picker rows: declared variables first (the most local), then the registry helpers. */
fun variableOptions(declared: List<DeclaredVariable>, helpers: List<TemplateHelperDto>): List<VariableOption> =
    declared.map { VariableOption(key = it.name, descriptionKey = null, sample = it.sample) } +
        helpers.map { VariableOption(key = it.key, descriptionKey = it.descriptionKey, sample = it.sample) }

/** Case-insensitive filter on the key; a blank [query] returns every option. */
fun filterVariableOptions(options: List<VariableOption>, query: String): List<VariableOption> {
    val needle: String = query.trim().lowercase()
    return if (needle.isEmpty()) options else options.filter { it.key.lowercase().contains(needle) }
}

// The index of the `{` that opens the fragment being typed before the cursor: nearest `{` to the left with no
// `}` or whitespace between it and the cursor. Null when the cursor is not inside such a fragment.
private fun braceStart(value: TextFieldValue): Int? {
    if (!value.selection.collapsed) return null
    var i: Int = value.selection.start - 1
    while (i >= 0) {
        val c: Char = value.text[i]
        if (c == '{') return i
        if (c == '}' || c.isWhitespace()) return null
        i--
    }
    return null
}

/** The text typed after an open `{` before the cursor (the filter), or null when no picker should be open. */
fun braceQuery(value: TextFieldValue): String? =
    braceStart(value)?.let { value.text.substring(it + 1, value.selection.start) }

/**
 * Inserts [token] (e.g. `{user.name}`) at the cursor. A `{fragment` typed just before the cursor is replaced by
 * the token; otherwise a selection is replaced, or the token goes at the caret. The cursor ends after the token.
 */
fun insertAtCursor(value: TextFieldValue, token: String): TextFieldValue {
    val start: Int = braceStart(value) ?: value.selection.min
    val end: Int = value.selection.max
    val text: String = value.text.substring(0, start) + token + value.text.substring(end)
    return TextFieldValue(text = text, selection = TextRange(start + token.length))
}
