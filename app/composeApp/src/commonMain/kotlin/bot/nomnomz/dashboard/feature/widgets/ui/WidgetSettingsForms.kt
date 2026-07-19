// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.widgets.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshots.SnapshotStateMap
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.Slider
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.Textarea
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.WidgetSettingsFieldDto
import bot.nomnomz.dashboard.core.network.WidgetSettingsSchemaDto
import bot.nomnomz.dashboard.core.network.WidgetSummary
import kotlin.math.max
import kotlin.math.roundToInt
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.contentOrNull
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.widgets_settings_cancel
import nomnomzbot.composeapp.generated.resources.widgets_settings_close
import nomnomzbot.composeapp.generated.resources.widgets_settings_error
import nomnomzbot.composeapp.generated.resources.widgets_settings_invalid_json
import nomnomzbot.composeapp.generated.resources.widgets_settings_listens_for
import nomnomzbot.composeapp.generated.resources.widgets_settings_loading
import nomnomzbot.composeapp.generated.resources.widgets_settings_save
import nomnomzbot.composeapp.generated.resources.widgets_settings_title
import org.jetbrains.compose.resources.stringResource

// The Overlays page's widget settings dialog. A first-party widget's `settings` is a free-form JSON object the
// overlay reads at render time; rather than hand-edit its Vue source, the operator configures it through a generic
// form driven ENTIRELY by the backend's typed settings schema (WidgetsController GET .../settings-schema). Adding a
// knob to a widget — or a whole new first-party widget — needs no change here: the schema grows and this form
// renders the new field. Control per field type: bool → switch, number → slider (when bounded) or numeric field,
// text → field, color → hex field + swatch, select → dropdown, multiselect → chips, json → raw-JSON textarea for a
// structural map/list the schema does not flatten.

// A tolerant parser for the raw-JSON fields the operator edits (goal colours, socials handles, …).
private val SettingsJson: Json = Json { isLenient = true }

/**
 * The settings dialog for [widget]. Loads the widget's typed schema via [loadSchema] (loading / error / form), then
 * renders the schema-driven form prefilled from the widget's current settings. [onSave] receives the assembled
 * settings object (every schema field written, any non-schema key preserved) to PUT.
 */
@Composable
internal fun WidgetSettingsDialog(
    widget: WidgetSummary,
    loadSchema: suspend () -> ApiResult<WidgetSettingsSchemaDto>,
    onDismiss: () -> Unit,
    onSave: (JsonObject) -> Unit,
) {
    var result: ApiResult<WidgetSettingsSchemaDto>? by remember(widget.id) { mutableStateOf(null) }
    LaunchedEffect(widget.id) { result = loadSchema() }

    when (val current: ApiResult<WidgetSettingsSchemaDto>? = result) {
        null -> InfoSettingsDialog(widget.name, stringResource(Res.string.widgets_settings_loading), onDismiss)
        is ApiResult.Failure ->
            InfoSettingsDialog(
                widget.name,
                stringResource(Res.string.widgets_settings_error, current.error.message),
                onDismiss,
            )
        is ApiResult.Ok -> LoadedSettingsDialog(widget, current.value, onDismiss, onSave)
    }
}

// A minimal dialog for the loading / error states — the schema-driven form is not yet (or cannot be) shown.
@Composable
private fun InfoSettingsDialog(widgetName: String, message: String, onDismiss: () -> Unit) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(text = stringResource(Res.string.widgets_settings_title, widgetName), style = typography.lg)
        },
        text = { Text(text = message, style = typography.sm, color = tokens.mutedForeground) },
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.widgets_settings_close), color = tokens.mutedForeground)
            }
        },
    )
}

// The per-field editable state — one snapshot map per value shape, seeded once from the widget's current settings.
private class FormState(
    val raw: SnapshotStateMap<String, String>, // text / color / number / json fields
    val bools: SnapshotStateMap<String, Boolean>,
    val selects: SnapshotStateMap<String, String>,
    val multis: SnapshotStateMap<String, List<String>>,
    val jsonErrors: SnapshotStateMap<String, Boolean>,
)

@Composable
private fun LoadedSettingsDialog(
    widget: WidgetSummary,
    schema: WidgetSettingsSchemaDto,
    onDismiss: () -> Unit,
    onSave: (JsonObject) -> Unit,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val settings: JsonObject? = widget.settings
    val invalidJson: String = stringResource(Res.string.widgets_settings_invalid_json)

    val state: FormState =
        remember(widget.id) {
            FormState(
                raw = mutableStateMapOf(),
                bools = mutableStateMapOf(),
                selects = mutableStateMapOf(),
                multis = mutableStateMapOf(),
                jsonErrors = mutableStateMapOf(),
            )
                .also { s ->
                    for (field in schema.fields) {
                        when (field.type) {
                            "bool" -> s.bools[field.key] = initialBool(field, settings)
                            "select" -> s.selects[field.key] = initialSelect(field, settings)
                            "multiselect" -> s.multis[field.key] = initialMulti(field, settings)
                            else -> s.raw[field.key] = initialRaw(field, settings)
                        }
                    }
                }
        }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(text = stringResource(Res.string.widgets_settings_title, widget.name), style = typography.lg)
        },
        text = {
            LazyColumn(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
                // Fields, grouped in their authored order (groupBy keeps first-seen order).
                schema.fields.groupBy { it.group }.forEach { (group, fields) ->
                    item(key = "group:$group") {
                        Text(text = group, style = typography.sm, color = tokens.mutedForeground)
                    }
                    items(items = fields, key = { it.key }) { field ->
                        FieldControl(
                            field = field,
                            rawValue = state.raw[field.key].orEmpty(),
                            onRawChange = {
                                state.raw[field.key] = it
                                if (field.type == "json") state.jsonErrors.remove(field.key)
                            },
                            boolValue = state.bools[field.key] ?: false,
                            onBoolChange = { state.bools[field.key] = it },
                            selectValue = state.selects[field.key].orEmpty(),
                            onSelectChange = { state.selects[field.key] = it },
                            multiValue = state.multis[field.key].orEmpty(),
                            onMultiToggle = { value ->
                                val cur: List<String> = state.multis[field.key].orEmpty()
                                state.multis[field.key] = if (value in cur) cur - value else cur + value
                            },
                            jsonError = state.jsonErrors[field.key] == true,
                            invalidJsonText = invalidJson,
                        )
                    }
                }
                // The widget's event wiring — read-only reference (intrinsic to the widget, not user config).
                if (schema.eventSubscriptions.isNotEmpty()) {
                    item(key = "event-subs") { EventSubscriptionsInfo(schema.eventSubscriptions) }
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    if (!validateJsonFields(schema, state)) return@Button
                    onSave(buildSettings(schema, state, settings))
                },
            ) {
                Text(stringResource(Res.string.widgets_settings_save))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.widgets_settings_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

// One field, rendered by its schema type. Unused callbacks/params for a given type are ignored.
@Composable
private fun FieldControl(
    field: WidgetSettingsFieldDto,
    rawValue: String,
    onRawChange: (String) -> Unit,
    boolValue: Boolean,
    onBoolChange: (Boolean) -> Unit,
    selectValue: String,
    onSelectChange: (String) -> Unit,
    multiValue: List<String>,
    onMultiToggle: (String) -> Unit,
    jsonError: Boolean,
    invalidJsonText: String,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    when (field.type) {
        "bool" ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(text = field.label, style = typography.base, color = tokens.cardForeground)
                    field.help?.takeIf { it.isNotBlank() }?.let {
                        Text(text = it, style = typography.xs, color = tokens.mutedForeground)
                    }
                }
                Switch(checked = boolValue, onCheckedChange = onBoolChange)
            }

        "number" -> {
            val minValue: Double? = field.min
            val maxLimit: Double? = field.max
            val stepSize: Double? = field.step
            if (minValue != null && maxLimit != null && stepSize != null) {
                val min: Float = minValue.toFloat()
                val maxValue: Float = maxLimit.toFloat()
                val step: Double = stepSize
                val steps: Int = max(0, ((maxValue - min) / step).roundToInt() - 1)
                val current: Float = (rawValue.toFloatOrNull() ?: min).coerceIn(min, maxValue)
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                    Text(
                        text = "${field.label}: ${formatNumber(current, step)}",
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                    Slider(
                        value = current,
                        onValueChange = { onRawChange(formatNumber(it, step)) },
                        valueRange = min..maxValue,
                        steps = steps,
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            } else {
                AppTextField(
                    value = rawValue,
                    onValueChange = onRawChange,
                    label = field.label,
                    supportingText = field.help,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        }

        "color" ->
            AppTextField(
                value = rawValue,
                onValueChange = onRawChange,
                label = field.label,
                supportingText = field.help,
                placeholder = "#RRGGBB",
                trailingIcon = { ColorSwatch(rawValue) },
                modifier = Modifier.fillMaxWidth(),
            )

        "select" ->
            SelectControl(
                label = field.label,
                options = field.options.orEmpty().map { it.value to it.label },
                selectedValue = selectValue,
                onSelect = onSelectChange,
            )

        "multiselect" ->
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(text = field.label, style = typography.sm, color = tokens.foreground)
                field.options.orEmpty().chunked(2).forEach { rowOptions ->
                    Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                        rowOptions.forEach { option ->
                            Badge(
                                selected = option.value in multiValue,
                                onClick = { onMultiToggle(option.value) },
                            ) {
                                Text(option.label, style = typography.xs)
                            }
                        }
                    }
                }
            }

        "json" ->
            Textarea(
                value = rawValue,
                onValueChange = onRawChange,
                label = field.label,
                supportingText = field.help,
                isError = jsonError,
                errorText = invalidJsonText,
                minLines = 2,
                monospace = true,
                modifier = Modifier.fillMaxWidth(),
            )

        else -> // text
            AppTextField(
                value = rawValue,
                onValueChange = onRawChange,
                label = field.label,
                supportingText = field.help,
                modifier = Modifier.fillMaxWidth(),
            )
    }
}

// A single-choice dropdown: a bordered trigger showing the selected option's label, opening a themed menu.
@Composable
private fun SelectControl(
    label: String,
    options: List<Pair<String, String>>,
    selectedValue: String,
    onSelect: (String) -> Unit,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    var expanded: Boolean by remember { mutableStateOf(false) }

    val selectedLabel: String =
        options.firstOrNull { it.first == selectedValue }?.second ?: selectedValue

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1_5)) {
        Text(text = label, style = typography.sm, color = tokens.foreground)
        Box {
            Row(
                modifier =
                    Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(tokens.radius.md))
                        .border(
                            width = spacing.s0_5 / 2,
                            color = tokens.border,
                            shape = RoundedCornerShape(tokens.radius.md),
                        )
                        .background(tokens.background)
                        .clickable { expanded = true }
                        .padding(horizontal = spacing.s3, vertical = spacing.s2),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = selectedLabel,
                    style = typography.sm,
                    color = tokens.foreground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(text = "▾", style = typography.sm, color = tokens.mutedForeground)
            }
            DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                options.forEach { (value, optionLabel) ->
                    DropdownMenuItem(
                        text = { Text(optionLabel) },
                        onClick = {
                            onSelect(value)
                            expanded = false
                        },
                    )
                }
            }
        }
    }
}

// A colour preview chip for a hex field; falls back to the muted token when the value is blank/unparseable.
@Composable
private fun ColorSwatch(value: String) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val color: Color = parseHexColor(value) ?: tokens.muted
    Box(
        modifier =
            Modifier
                .size(spacing.s4)
                .clip(RoundedCornerShape(tokens.radius.sm))
                .border(width = spacing.s0_5 / 2, color = tokens.border, shape = RoundedCornerShape(tokens.radius.sm))
                .background(color)
    )
}

// The widget's default event topics, shown read-only as chips (its data wiring is intrinsic, not user-editable).
@Composable
private fun EventSubscriptionsInfo(events: List<String>) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(
            text = stringResource(Res.string.widgets_settings_listens_for),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        events.chunked(3).forEach { rowEvents ->
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                rowEvents.forEach { event -> Badge { Text(event, style = typography.xs) } }
            }
        }
    }
}

// ── value seeding + assembly ─────────────────────────────────────────────────────

private fun initialRaw(field: WidgetSettingsFieldDto, settings: JsonObject?): String {
    val element: JsonElement? = settings?.get(field.key) ?: field.default
    return when {
        element == null || element is JsonNull -> ""
        element is JsonPrimitive -> element.contentOrNull ?: ""
        else -> element.toString() // json fields: a map/array rendered as compact JSON
    }
}

private fun initialBool(field: WidgetSettingsFieldDto, settings: JsonObject?): Boolean {
    val element: JsonElement? = settings?.get(field.key) ?: field.default
    return (element as? JsonPrimitive)?.booleanOrNull ?: false
}

private fun initialSelect(field: WidgetSettingsFieldDto, settings: JsonObject?): String {
    val element: JsonElement? = settings?.get(field.key) ?: field.default
    val value: String? = (element as? JsonPrimitive)?.contentOrNull
    return value?.takeIf { it.isNotEmpty() } ?: field.options?.firstOrNull()?.value ?: ""
}

private fun initialMulti(field: WidgetSettingsFieldDto, settings: JsonObject?): List<String> {
    val element: JsonElement = settings?.get(field.key) ?: field.default ?: return emptyList()
    val array: JsonArray = (element as? JsonArray) ?: return emptyList()
    return array.mapNotNull { (it as? JsonPrimitive)?.contentOrNull }
}

// Parse every json field's raw text; mark the ones that don't parse and report whether all are valid.
private fun validateJsonFields(schema: WidgetSettingsSchemaDto, state: FormState): Boolean {
    var allValid = true
    for (field in schema.fields.filter { it.type == "json" }) {
        val text: String = state.raw[field.key].orEmpty().trim()
        if (text.isEmpty()) continue
        val valid: Boolean =
            runCatching { SettingsJson.parseToJsonElement(text) }.isSuccess
        state.jsonErrors[field.key] = !valid
        if (!valid) allValid = false
    }
    return allValid
}

// Assemble the settings object: preserve any non-schema key the widget already carries, then write every field.
private fun buildSettings(
    schema: WidgetSettingsSchemaDto,
    state: FormState,
    existing: JsonObject?,
): JsonObject {
    val merged: LinkedHashMap<String, JsonElement> = LinkedHashMap()
    existing?.forEach { (key, value) -> merged[key] = value }

    for (field in schema.fields) {
        merged[field.key] =
            when (field.type) {
                "bool" -> JsonPrimitive(state.bools[field.key] ?: false)
                "select" -> JsonPrimitive(state.selects[field.key].orEmpty())
                "multiselect" ->
                    JsonArray(state.multis[field.key].orEmpty().map { JsonPrimitive(it) })
                "number" -> numberElement(state.raw[field.key].orEmpty(), field)
                "json" -> jsonElement(state.raw[field.key].orEmpty(), field)
                else -> JsonPrimitive(state.raw[field.key].orEmpty().trim()) // text, color
            }
    }
    return JsonObject(merged)
}

// A numeric setting: decimal when the field's step is fractional or the text carries a point, else an integer.
// Blank/unparseable falls back to the catalogue default (or 0) so a key is never dropped.
private fun numberElement(raw: String, field: WidgetSettingsFieldDto): JsonElement {
    val text: String = raw.trim()
    val fallback: JsonElement = field.default ?: JsonPrimitive(0)
    if (text.isEmpty()) return fallback
    val parsed: Double = text.toDoubleOrNull() ?: return fallback
    val step: Double? = field.step
    val decimal: Boolean = (step != null && step < 1.0) || text.contains('.')
    return if (decimal) JsonPrimitive(parsed) else JsonPrimitive(parsed.toLong())
}

// A structural (map/list) setting from its raw JSON text (already validated); blank/invalid falls back to default.
private fun jsonElement(raw: String, field: WidgetSettingsFieldDto): JsonElement {
    val text: String = raw.trim()
    val fallback: JsonElement = field.default ?: JsonObject(emptyMap())
    if (text.isEmpty()) return fallback
    return runCatching { SettingsJson.parseToJsonElement(text) }.getOrDefault(fallback)
}

private fun formatNumber(value: Float, step: Double): String =
    if (step < 1.0) ((value * 100).roundToInt() / 100.0).toString() else value.roundToInt().toString()

// Parse "#RGB" / "#RRGGBB" / "#AARRGGBB" (with or without the leading #) to a Color, or null if it is not a hex.
private fun parseHexColor(value: String): Color? {
    val hex: String = value.trim().removePrefix("#")
    val argb: String =
        when (hex.length) {
            3 -> "FF" + hex.map { "$it$it" }.joinToString("")
            6 -> "FF$hex"
            8 -> hex
            else -> return null
        }
    val packed: Long = argb.toLongOrNull(16) ?: return null
    return Color(packed)
}
