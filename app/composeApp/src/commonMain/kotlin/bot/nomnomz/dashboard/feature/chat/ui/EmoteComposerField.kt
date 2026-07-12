// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chat.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.input.key.KeyEvent
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.media.AnimatedNetworkImage
import bot.nomnomz.dashboard.core.network.ChatEmoteCatalogue
import kotlin.math.ceil
import kotlin.math.roundToInt

// The chat composer's input with TRUE inline emotes (chat-client.md §3.1): a Foundation text field, styled to match
// the design-system Input, whose recognised emote codes and Unicode emoji clusters render AS THEIR IMAGES inside the
// editable line — not in a separate preview strip. It works by reserving a blank run for each image (see
// [buildComposerInlineTransform]) and painting the image over that run from the field's own [TextLayoutResult], so
// the caret and selection operate on the real text and never desync. The field is multi-line/soft-wrapping, so
// there is no horizontal scroll to keep the overlay in sync with.
@Composable
internal fun EmoteComposerField(
    value: TextFieldValue,
    onValueChange: (TextFieldValue) -> Unit,
    emoteByCode: Map<String, ChatEmoteCatalogue>,
    placeholder: String,
    enabled: Boolean,
    onPreviewKey: (KeyEvent) -> Boolean,
    modifier: Modifier = Modifier,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val density = LocalDensity.current

    val textStyle: TextStyle = typography.sm.copy(color = tokens.foreground)

    val interactionSource: MutableInteractionSource = remember { MutableInteractionSource() }
    val focused: Boolean by interactionSource.collectIsFocusedAsState()
    val borderColor = if (focused) tokens.ring else tokens.border
    val shape = RoundedCornerShape(tokens.radius.md)

    // Size the inline image to ~1.45× the font height (matching the feed's inline-emote scale) and reserve a blank
    // run about that wide. Space advance is measured trailing-space-safe (a lone " " has zero layout width).
    val measurer = rememberTextMeasurer()
    val emoteSizePx: Float = with(density) { textStyle.fontSize.toPx() } * EmoteScale
    val spaceWidthPx: Float =
        remember(textStyle) {
            val withSpace: Int = measurer.measure(AnnotatedString("x x"), textStyle).size.width
            val withoutSpace: Int = measurer.measure(AnnotatedString("xx"), textStyle).size.width
            (withSpace - withoutSpace).toFloat().coerceAtLeast(1f)
        }
    val spacesPerImage: Int =
        remember(emoteSizePx, spaceWidthPx) { ceil(emoteSizePx / spaceWidthPx).toInt().coerceIn(1, 16) }

    val transform: ComposerInlineTransform =
        remember(value.text, emoteByCode, spacesPerImage) {
            buildComposerInlineTransform(value.text, emoteByCode, spacesPerImage)
        }

    var layoutResult: TextLayoutResult? by remember { mutableStateOf(null) }
    val emoteSizeDp: Dp = with(density) { emoteSizePx.toDp() }

    BasicTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        textStyle = textStyle,
        cursorBrush = SolidColor(tokens.primary),
        visualTransformation = composerVisualTransformation(transform),
        onTextLayout = { layoutResult = it },
        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send),
        interactionSource = interactionSource,
        maxLines = ComposerMaxLines,
        modifier = modifier.onPreviewKeyEvent(onPreviewKey),
        decorationBox = { innerTextField ->
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .border(width = FieldBorderWidth, color = borderColor, shape = shape)
                    .clip(shape)
                    .background(tokens.background)
                    .padding(horizontal = spacing.s3, vertical = spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(modifier = Modifier.weight(1f)) {
                    if (value.text.isEmpty()) {
                        Text(
                            text = placeholder,
                            style = typography.sm.copy(color = tokens.mutedForeground),
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                    // The editable text with the inline images painted over their reserved blank runs. Both share
                    // the same top-left origin, so a bounding box from the field's layout places each image exactly.
                    Box {
                        innerTextField()
                        val layout: TextLayoutResult? = layoutResult
                        if (layout != null) {
                            val displayLength: Int = layout.layoutInput.text.length
                            transform.images.forEach { image ->
                                if (image.transformedStart < displayLength) {
                                    val boundingBox: Rect = layout.getBoundingBox(image.transformedStart)
                                    val topPx: Float = boundingBox.top + (boundingBox.height - emoteSizePx) / 2f
                                    AnimatedNetworkImage(
                                        url = image.imageUrl,
                                        contentDescription = null,
                                        modifier = Modifier
                                            .offset {
                                                IntOffset(boundingBox.left.roundToInt(), topPx.roundToInt())
                                            }
                                            .size(emoteSizeDp),
                                    )
                                }
                            }
                        }
                    }
                }
            }
        },
    )
}

// 1dp border stroke — a fixed visual stroke, not a layout spacing token (mirrors the design-system Input).
private val FieldBorderWidth: Dp = 1.dp

// Inline image size relative to the font height; matches the feed's inline-emote scale so composer and feed agree.
private const val EmoteScale: Float = 1.45f

// The composer grows up to this many wrapped lines before scrolling — a long draft stays readable without a
// horizontal scroll (which the inline overlay would otherwise have to track).
private const val ComposerMaxLines: Int = 6
