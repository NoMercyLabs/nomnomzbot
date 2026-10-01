// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.theme

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.test.currentTime
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNull

// A fresh visitor's dashboard died on its login page (2026-10-01): the 17 MB CJK fallback face lost its
// transfer part-way, the browser's "Failed to fetch" escaped the font preload, and the recomposer died with it.
// A plain Throwable stands in for the Kotlin/Wasm JsException, which is not an Exception either.
class FallbackFontReadTest {

    private val face: ByteArray = byteArrayOf(0, 1, 0, 0)

    @Test
    fun a_transfer_that_drops_twice_is_retried_until_the_face_arrives() = runTest {
        var calls: Int = 0

        val bytes: ByteArray? = readFallbackFont {
            calls++
            if (calls < 3) throw Throwable("Failed to fetch")
            face
        }

        assertContentEquals(face, bytes)
        assertEquals(3, calls)
        assertEquals(7_000, currentTime)
    }

    @Test
    fun a_face_that_never_arrives_is_given_up_instead_of_crashing_the_caller() = runTest {
        var calls: Int = 0

        val bytes: ByteArray? = readFallbackFont {
            calls++
            throw Throwable("Failed to fetch")
        }

        assertNull(bytes)
        assertEquals(3, calls)
    }

    @Test
    fun a_cancelled_read_keeps_cancelling_and_is_not_retried() = runTest {
        var calls: Int = 0

        assertFailsWith<CancellationException> {
            readFallbackFont {
                calls++
                throw CancellationException("left the composition")
            }
        }

        assertEquals(1, calls)
    }
}
