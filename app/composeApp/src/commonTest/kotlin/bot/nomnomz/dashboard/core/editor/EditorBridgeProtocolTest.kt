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

import bot.nomnomz.dashboard.core.network.BuildError
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
    fun requestCloseIsAHostToPageMessageTheDecoderIgnoresWhenEchoedBack() {
        assertEquals("""{"type":"nnz:editor:requestClose"}""", EditorBridgeProtocol.requestClose())
        assertNull(EditorBridgeProtocol.decode(EditorBridgeProtocol.requestClose()))
    }

    @Test
    fun compiledReplyRoundTripsThroughJsonUnchanged() {
        val encoded: String = EditorBridgeProtocol.compiled(CompileFeedback(ok = true, message = "Built \"v3\"\nok"))

        assertEquals("""{"type":"nnz:editor:compiled","ok":true,"message":"Built \"v3\"\nok"}""", encoded)
    }

    @Test
    fun aCompiledReplyCarriesEachErrorWithItsFileLineAndColumn() {
        val encoded: String =
            EditorBridgeProtocol.compiled(
                CompileFeedback(
                    ok = false,
                    message = "2 errors",
                    errors =
                        listOf(
                            BuildError(code = "build", message = "bad token", file = "lib.ts", line = 2, column = 18),
                            BuildError(code = "build", message = "no entry", file = null, line = null, column = null),
                        ),
                )
            )

        assertEquals(
            """{"type":"nnz:editor:compiled","ok":false,"message":"2 errors","errors":[""" +
                """{"code":"build","message":"bad token","file":"lib.ts","line":2,"column":18},""" +
                """{"code":"build","message":"no entry"}]}""",
            encoded,
        )
    }

    @Test
    fun aCompiledReplyWithoutErrorsHasNoErrorsField() {
        val encoded: String = EditorBridgeProtocol.compiled(CompileFeedback(ok = false, message = "failed"))

        assertEquals("""{"type":"nnz:editor:compiled","ok":false,"message":"failed"}""", encoded)
    }

    @Test
    fun aTestRunResultCarriesTheErrorPositionAsAnErrorForTheEditorToUnderline() {
        val result: EditorTestRunResult =
            EditorTestRunResult(
                success = false,
                durationMs = 4,
                hostCallCount = 0,
                error = "boom (line 3, column 5)",
                chatOutput = emptyList(),
                effects = emptyList(),
                errors = listOf(BuildError(code = "runtime", message = "boom", file = "index.ts", line = 3, column = 5)),
            )

        val encoded: String = EditorBridgeProtocol.testRunResult(result)

        assertTrue(
            encoded.contains(
                """"errors":[{"code":"runtime","message":"boom","file":"index.ts","line":3,"column":5}]"""
            ),
            encoded,
        )
    }

    @Test
    fun aTestRunResultWithoutAnErrorPositionHasNoErrorsField() {
        val result: EditorTestRunResult =
            EditorTestRunResult(
                success = true,
                durationMs = 4,
                hostCallCount = 0,
                error = null,
                chatOutput = emptyList(),
                effects = emptyList(),
            )

        assertTrue(!EditorBridgeProtocol.testRunResult(result).contains("\"errors\""))
    }
}
