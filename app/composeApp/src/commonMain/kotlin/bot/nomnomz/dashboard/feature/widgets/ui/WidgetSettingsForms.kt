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

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Slider
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.WidgetSummary
import kotlin.math.roundToInt
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.floatOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonPrimitive
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.widgets_settings_cancel
import nomnomzbot.composeapp.generated.resources.widgets_settings_chatbox_background
import nomnomzbot.composeapp.generated.resources.widgets_settings_chatbox_background_hint
import nomnomzbot.composeapp.generated.resources.widgets_settings_chatbox_background_opacity
import nomnomzbot.composeapp.generated.resources.widgets_settings_chatbox_font_family
import nomnomzbot.composeapp.generated.resources.widgets_settings_chatbox_font_family_hint
import nomnomzbot.composeapp.generated.resources.widgets_settings_chatbox_font_size
import nomnomzbot.composeapp.generated.resources.widgets_settings_chatbox_show_timestamps
import nomnomzbot.composeapp.generated.resources.widgets_settings_save
import nomnomzbot.composeapp.generated.resources.widgets_settings_title
import org.jetbrains.compose.resources.stringResource

// The per-widget-type typed-settings registry. A widget's `settings` is a free-form JSON object the overlay reads
// at render time; for widget types whose knobs are known and backend-supported we render a TYPED form instead of
// raw JSON. [typedSettingsFormFor] returns the matching form for a widget (or null when none is registered), so
// [WidgetsScreen] only shows the "Settings" affordance where a typed form exists. Adding a new typed form is a
// single entry here — no change to the row plumbing.
//
// Discrimination is by the settings SIGNATURE (the keys present), not the display name (which the operator can
// rename freely): a widget carrying the chat_box config keys is a chat_box, whatever it is called.
internal fun interface WidgetSettingsForm {
    @Composable
    fun Render(widget: WidgetSummary, onDismiss: () -> Unit, onSave: (JsonObject) -> Unit)
}

/** Return the typed settings form registered for [widget], or null when the widget has no typed form. */
internal fun typedSettingsFormFor(widget: WidgetSummary): WidgetSettingsForm? {
    val settings: JsonObject = widget.settings ?: return null
    // chat_box: identified by its distinctive config keys (chat_box.vue's ChatBoxConfig).
    if (settings.containsKey("showTimestamps") && settings.containsKey("backgroundOpacity")) {
        return WidgetSettingsForm { w, onDismiss, onSave -> ChatBoxSettingsDialog(w, onDismiss, onSave) }
    }
    return null
}

// ── chat_box typed settings ─────────────────────────────────────────────────────

// The subset of chat_box config the operator edits through the typed form. The full settings object carries more
// (theme, maxMessages, badges, …) which we PRESERVE untouched — only these five keys are written by the form.
private const val KeyFontFamily: String = "fontFamily"
private const val KeyFontSize: String = "fontSize"
private const val KeyBackground: String = "background"
private const val KeyBackgroundOpacity: String = "backgroundOpacity"
private const val KeyShowTimestamps: String = "showTimestamps"

private const val FontSizeMin: Int = 8
private const val FontSizeMax: Int = 48
private const val FontSizeDefault: Int = 16
private const val BackgroundOpacityDefault: Float = 0.82f

@Composable
private fun ChatBoxSettingsDialog(
    widget: WidgetSummary,
    onDismiss: () -> Unit,
    onSave: (JsonObject) -> Unit,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val settings: JsonObject = widget.settings ?: JsonObject(emptyMap())

    var fontFamily: String by remember {
        mutableStateOf(settings[KeyFontFamily]?.jsonPrimitive?.contentOrNull ?: "")
    }
    var fontSize: Float by remember {
        mutableStateOf((settings[KeyFontSize]?.jsonPrimitive?.intOrNull ?: FontSizeDefault).toFloat())
    }
    var background: String by remember {
        mutableStateOf(settings[KeyBackground]?.jsonPrimitive?.contentOrNull ?: "")
    }
    var backgroundOpacity: Float by remember {
        mutableStateOf(settings[KeyBackgroundOpacity]?.jsonPrimitive?.floatOrNull ?: BackgroundOpacityDefault)
    }
    var showTimestamps: Boolean by remember {
        mutableStateOf(settings[KeyShowTimestamps]?.jsonPrimitive?.booleanOrNull ?: false)
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.widgets_settings_title, widget.name), style = typography.lg) },
        text = {
            LazyColumn(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
                item {
                    AppTextField(
                        value = fontFamily,
                        onValueChange = { fontFamily = it },
                        label = stringResource(Res.string.widgets_settings_chatbox_font_family),
                        supportingText = stringResource(Res.string.widgets_settings_chatbox_font_family_hint),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
                item {
                    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                        Text(
                            text = stringResource(
                                Res.string.widgets_settings_chatbox_font_size,
                                fontSize.roundToInt(),
                            ),
                            style = typography.sm,
                            color = tokens.mutedForeground,
                        )
                        Slider(
                            value = fontSize,
                            onValueChange = { fontSize = it },
                            valueRange = FontSizeMin.toFloat()..FontSizeMax.toFloat(),
                            steps = FontSizeMax - FontSizeMin - 1,
                            modifier = Modifier.fillMaxWidth(),
                        )
                    }
                }
                item {
                    AppTextField(
                        value = background,
                        onValueChange = { background = it },
                        label = stringResource(Res.string.widgets_settings_chatbox_background),
                        supportingText = stringResource(Res.string.widgets_settings_chatbox_background_hint),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
                item {
                    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                        Text(
                            text = stringResource(
                                Res.string.widgets_settings_chatbox_background_opacity,
                                (backgroundOpacity * 100).roundToInt(),
                            ),
                            style = typography.sm,
                            color = tokens.mutedForeground,
                        )
                        Slider(
                            value = backgroundOpacity,
                            onValueChange = { backgroundOpacity = it },
                            valueRange = 0f..1f,
                            modifier = Modifier.fillMaxWidth(),
                        )
                    }
                }
                item {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween,
                    ) {
                        Text(
                            text = stringResource(Res.string.widgets_settings_chatbox_show_timestamps),
                            style = typography.base,
                            color = tokens.cardForeground,
                        )
                        Switch(checked = showTimestamps, onCheckedChange = { showTimestamps = it })
                    }
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    // Preserve every unedited key, overwrite only the five the form owns.
                    val merged: MutableMap<String, kotlinx.serialization.json.JsonElement> =
                        settings.toMutableMap()
                    merged[KeyFontFamily] = JsonPrimitive(fontFamily.trim())
                    merged[KeyFontSize] = JsonPrimitive(fontSize.roundToInt())
                    merged[KeyBackground] = JsonPrimitive(background.trim())
                    merged[KeyBackgroundOpacity] =
                        JsonPrimitive((backgroundOpacity * 100).roundToInt() / 100.0)
                    merged[KeyShowTimestamps] = JsonPrimitive(showTimestamps)
                    onSave(JsonObject(merged))
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
