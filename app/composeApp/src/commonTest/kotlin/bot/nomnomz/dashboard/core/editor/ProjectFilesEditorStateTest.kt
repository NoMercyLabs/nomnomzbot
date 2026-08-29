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
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// S-CODE-EDITOR-d: proves the multi-file editor's core tab-switching rule -- an edit made to one file survives a
// round trip through another file, because the outgoing file's content is flushed into the map on every switch,
// not lost. Both the desktop (Swing) and web (Monaco) editors delegate this bookkeeping to
// [ProjectFilesEditorState]; this test exercises that shared logic directly rather than through either UI toolkit.
class ProjectFilesEditorStateTest {
    @Test
    fun `switching tabs preserves each file's edited content`() {
        val state =
            ProjectFilesEditorState(
                initialFiles = mapOf("index.ts" to "// entry", "lib/util.ts" to "// util"),
                entryPath = "index.ts",
            )

        // Edit file A (the active/entry file), then switch to file B.
        state.select("lib/util.ts", outgoingContent = "export const a = 1; // edited A")
        assertEquals("lib/util.ts", state.active)
        assertEquals("// util", state.activeContent())

        // Edit file B, then switch back to A.
        state.select("index.ts", outgoingContent = "export const b = 2; // edited B")
        assertEquals("index.ts", state.active)

        // A's edit made before the first switch must still be there.
        assertEquals("export const a = 1; // edited A", state.activeContent())

        // And a full snapshot (what "Save & Compile" round-trips to the server) carries BOTH edits, including the
        // one sitting unflushed in the "editor widget" for the currently active file.
        val snapshot: Map<String, String> = state.snapshot(outgoingContent = "export const a = 1; // edited A again")
        assertEquals("export const a = 1; // edited A again", snapshot["index.ts"])
        assertEquals("export const b = 2; // edited B", snapshot["lib/util.ts"])
    }

    @Test
    fun `entry file cannot be renamed or deleted`() {
        val state = ProjectFilesEditorState(initialFiles = mapOf("index.ts" to "x"), entryPath = "index.ts")

        assertFalse(state.renameActive("main.ts", outgoingContent = "x"))
        assertFalse(state.deleteActive())
        assertEquals(listOf("index.ts"), state.paths)
    }

    @Test
    fun `adding then renaming a helper file preserves its content and updates the active pointer`() {
        val state = ProjectFilesEditorState(initialFiles = mapOf("index.ts" to "entry"), entryPath = "index.ts")

        assertTrue(state.addFile("lib/helper.ts", outgoingContent = "entry"))
        state.edit("export function helper() {}")
        assertEquals("lib/helper.ts", state.active)

        assertTrue(state.renameActive("lib/utils.ts", outgoingContent = "export function helper() {}"))
        assertEquals("lib/utils.ts", state.active)
        assertEquals("export function helper() {}", state.content("lib/utils.ts"))
        assertEquals(listOf("index.ts", "lib/utils.ts"), state.paths)
    }
}
