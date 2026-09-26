// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.editor

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

// Decoding is the trust edge of the bridge: only page-to-host messages get through, and only the fields that
// belong to their type, whatever else the page (or anything else posting on the same window) sends.
class EditorBridgeProtocolTest {
    @Test
    fun saveKeepsItsFilesExactly() {
        val message: EditorInboundMessage? =
            EditorBridgeProtocol.decode("""{"type":"nnz:editor:save","files":{"a.ts":"line1\nline2 \"q\""}}""")

        assertEquals(EditorBridgeProtocol.SAVE, message?.type)
        assertEquals(mapOf("a.ts" to "line1\nline2 \"q\""), message?.files)
    }

    @Test
    fun filesOnAnyOtherTypeAreDroppedSoTheyCannotReachCompile() {
        val message: EditorInboundMessage? =
            EditorBridgeProtocol.decode("""{"type":"nnz:editor:close","files":{"a.ts":"evil"},"versionId":"v1"}""")

        assertEquals(EditorBridgeProtocol.CLOSE, message?.type)
        assertTrue(message!!.files.isEmpty())
        assertEquals("", message.versionId)
    }

    @Test
    fun hostToPageTypesEchoedBackAreIgnored() {
        assertNull(EditorBridgeProtocol.decode("""{"type":"nnz:editor:open","payload":{}}"""))
        assertNull(EditorBridgeProtocol.decode("""{"type":"nnz:editor:compiled","ok":true,"message":"x"}"""))
    }

    @Test
    fun foreignAndMalformedTrafficIsIgnored() {
        assertNull(EditorBridgeProtocol.decode("""{"type":"webpackOk"}"""))
        assertNull(EditorBridgeProtocol.decode("""{"no":"type"}"""))
        assertNull(EditorBridgeProtocol.decode("not json"))
    }

    @Test
    fun compiledReplyRoundTripsThroughJsonUnchanged() {
        val encoded: String = EditorBridgeProtocol.compiled(CompileFeedback(ok = true, message = "Built \"v3\"\nok"))

        assertEquals("""{"type":"nnz:editor:compiled","ok":true,"message":"Built \"v3\"\nok"}""", encoded)
    }
}
