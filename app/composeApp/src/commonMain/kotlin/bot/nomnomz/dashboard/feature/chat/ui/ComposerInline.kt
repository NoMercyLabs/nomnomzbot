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

import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.input.OffsetMapping
import androidx.compose.ui.text.input.TransformedText
import androidx.compose.ui.text.input.VisualTransformation
import bot.nomnomz.dashboard.core.media.EmojiSpan
import bot.nomnomz.dashboard.core.media.tokenizeEmoji
import bot.nomnomz.dashboard.core.network.ChatEmoteCatalogue

// The engine behind the composer's TRUE inline emotes (chat-client.md §3.1): recognised emote codes and Unicode
// emoji clusters are replaced, in the editable field itself, by a blank run whose width matches the image, and the
// image is painted over that run. Because the underlying text keeps the real characters and only the DISPLAY is
// blanked, the caret and selection stay perfectly correct — the trap that sank the old separate-preview approach.
//
// This file is the pure, unit-tested half: draft → (display string, image placements, offset mapping). The
// Compose rendering half lives in [EmoteComposerField]. Kept off-Compose so the offset mapping — the part that
// crashes the whole field if it is off by one — is provable in [ComposerInlineTest] rather than only at runtime.

// One image to overlay on the field: the DISPLAY-space offset where its reserved blank run begins, and the URL of
// the emote/emoji image to paint there.
internal data class ComposerInlineImage(
    val transformedStart: Int,
    val imageUrl: String,
)

// The transform of one draft: the width-reserved [display] string (codes/emoji → blank runs), the [images] to
// overlay, and the two offset-mapping tables. The tables are full lookup arrays (length = text length + 1) so the
// mapping is monotonic by construction and every lookup is a bounds-clamped array read — never an off-by-one throw.
internal class ComposerInlineTransform(
    val display: String,
    val images: List<ComposerInlineImage>,
    private val originalToTransformed: IntArray,
    private val transformedToOriginal: IntArray,
) {
    fun mapOriginalToTransformed(offset: Int): Int =
        originalToTransformed[offset.coerceIn(0, originalToTransformed.size - 1)]

    fun mapTransformedToOriginal(offset: Int): Int =
        transformedToOriginal[offset.coerceIn(0, transformedToOriginal.size - 1)]
}

// A contiguous run of the draft: plain text kept verbatim, or an image (an emote code / emoji cluster) to blank.
private sealed interface ComposerSegment {
    val start: Int
    val end: Int

    data class Text(override val start: Int, override val end: Int) : ComposerSegment

    data class Image(override val start: Int, override val end: Int, val imageUrl: String) : ComposerSegment
}

// Split [draft] into ordered, contiguous, gap-free segments: each whitespace-delimited word that matches a
// catalogue emote code (case-insensitive) becomes one image run; every other word is emoji-tokenized so its Unicode
// emoji clusters become image runs and the rest stays text; whitespace stays text. Covering [0, length) exactly is
// what lets the mapping tables below be built by a single forward walk.
private fun segmentComposerDraft(
    draft: String,
    emoteByCode: Map<String, ChatEmoteCatalogue>,
): List<ComposerSegment> {
    val segments: MutableList<ComposerSegment> = ArrayList()
    val n: Int = draft.length
    var i = 0
    while (i < n) {
        if (draft[i].isWhitespace()) {
            val start: Int = i
            while (i < n && draft[i].isWhitespace()) i++
            segments.add(ComposerSegment.Text(start, i))
        } else {
            val start: Int = i
            while (i < n && !draft[i].isWhitespace()) i++
            val word: String = draft.substring(start, i)
            val emote: ChatEmoteCatalogue? = emoteByCode[word.lowercase()]
            val emoteUrl: String? =
                emote?.let { it.urls["2"] ?: it.urls["1"] ?: it.urls.values.firstOrNull() }
            if (emote != null && emoteUrl != null) {
                segments.add(ComposerSegment.Image(start, i, emoteUrl))
            } else {
                var off: Int = start
                for (span in tokenizeEmoji(word)) {
                    when (span) {
                        is EmojiSpan.Text -> {
                            segments.add(ComposerSegment.Text(off, off + span.text.length))
                            off += span.text.length
                        }
                        is EmojiSpan.Emoji -> {
                            segments.add(ComposerSegment.Image(off, off + span.raw.length, span.imageUrl))
                            off += span.raw.length
                        }
                    }
                }
            }
        }
    }
    return segments
}

// Build the inline transform for [draft]: every image segment is replaced by [spacesPerImage] blanks (sized upstream
// so the run's width ≈ the image), and the mapping tables collapse the code's characters onto the run start (caret
// snaps to just before the emote) while text maps one-to-one. [spacesPerImage] is clamped to ≥ 1 so an image always
// reserves at least one caret stop.
internal fun buildComposerInlineTransform(
    draft: String,
    emoteByCode: Map<String, ChatEmoteCatalogue>,
    spacesPerImage: Int,
): ComposerInlineTransform {
    val blanks: Int = spacesPerImage.coerceAtLeast(1)
    val segments: List<ComposerSegment> = segmentComposerDraft(draft, emoteByCode)

    val display: StringBuilder = StringBuilder(draft.length)
    val originalToTransformed: IntArray = IntArray(draft.length + 1)
    val transformedToOriginal: MutableList<Int> = ArrayList(draft.length)
    val images: MutableList<ComposerInlineImage> = ArrayList()

    for (segment in segments) {
        when (segment) {
            is ComposerSegment.Text ->
                for (j in segment.start until segment.end) {
                    originalToTransformed[j] = display.length
                    transformedToOriginal.add(j)
                    display.append(draft[j])
                }
            is ComposerSegment.Image -> {
                val runStart: Int = display.length
                for (j in segment.start until segment.end) originalToTransformed[j] = runStart
                repeat(blanks) {
                    transformedToOriginal.add(segment.start)
                    display.append(' ')
                }
                images.add(ComposerInlineImage(transformedStart = runStart, imageUrl = segment.imageUrl))
            }
        }
    }
    // Terminal entries: end-of-original maps to end-of-display and vice versa (OffsetMapping requires both ends).
    originalToTransformed[draft.length] = display.length
    val transformedToOriginalArray: IntArray = IntArray(display.length + 1)
    for (idx in 0 until display.length) transformedToOriginalArray[idx] = transformedToOriginal[idx]
    transformedToOriginalArray[display.length] = draft.length

    return ComposerInlineTransform(
        display = display.toString(),
        images = images,
        originalToTransformed = originalToTransformed,
        transformedToOriginal = transformedToOriginalArray,
    )
}

// Wrap a prebuilt [transform] as a Compose [VisualTransformation]. The field's value and this transformation are
// derived from the same draft in one recomposition, so the incoming text always corresponds to [transform.display].
internal fun composerVisualTransformation(transform: ComposerInlineTransform): VisualTransformation =
    VisualTransformation {
        TransformedText(
            AnnotatedString(transform.display),
            object : OffsetMapping {
                override fun originalToTransformed(offset: Int): Int = transform.mapOriginalToTransformed(offset)

                override fun transformedToOriginal(offset: Int): Int = transform.mapTransformedToOriginal(offset)
            },
        )
    }
