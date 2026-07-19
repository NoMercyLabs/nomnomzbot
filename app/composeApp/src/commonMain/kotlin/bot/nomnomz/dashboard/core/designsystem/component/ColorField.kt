// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens

// The design-system control for a hex colour value (e.g. a Twitch reward's background colour): a text field
// carrying the `#RRGGBB` string with a live preview swatch at the trailing edge. Wraps [AppTextField] so it
// inherits the field's label/error/help affordances; the swatch previews whatever the value currently parses
// to, falling back to the muted token when it is blank or not a hex. Shared so any screen that edits a colour
// (widget settings, rewards) renders the same control rather than re-inventing a hex input.
@Composable
fun ColorField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    modifier: Modifier = Modifier,
    isError: Boolean = false,
    supportingText: String? = null,
) {
    AppTextField(
        value = value,
        onValueChange = onValueChange,
        label = label,
        isError = isError,
        supportingText = supportingText,
        placeholder = "#RRGGBB",
        keyboardOptions = KeyboardOptions.Default,
        trailingIcon = { ColorSwatch(value) },
        modifier = modifier,
    )
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
                .border(
                    width = spacing.s0_5 / 2,
                    color = tokens.border,
                    shape = RoundedCornerShape(tokens.radius.sm),
                )
                .background(color)
    )
}

// Parse "#RGB" / "#RRGGBB" / "#AARRGGBB" (with or without the leading #) to a Color, or null if it is not a hex.
fun parseHexColor(value: String): Color? {
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
