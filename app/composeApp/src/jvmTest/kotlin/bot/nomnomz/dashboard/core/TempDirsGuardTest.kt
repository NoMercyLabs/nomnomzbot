// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core

import java.io.File
import kotlin.test.Test
import kotlin.test.assertTrue

// Guards against temp folders that pile up in the OS temp dir: a jvmTest that creates a temp
// directory or file must go through [TempDirs], which deletes everything when the test ends.
class TempDirsGuardTest {

    private val forbiddenCall = Regex("createTemp(Directory|File)\\b")

    private fun jvmTestSourceRoot(): File {
        val candidates: List<File> = listOf(File("src/jvmTest/kotlin"), File("composeApp/src/jvmTest/kotlin"))
        return candidates.firstOrNull { it.isDirectory } ?: error("jvmTest source root not found from ${File(".").absolutePath}")
    }

    @Test
    fun `no jvmTest source creates a temp folder or file outside the shared TempDirs helper`() {
        val offenders: List<String> =
            jvmTestSourceRoot()
                .walkTopDown()
                .filter { it.isFile && it.extension == "kt" && it.name != "TempDirs.kt" }
                .flatMap { file ->
                    file.readLines().mapIndexedNotNull { index, line ->
                        if (forbiddenCall.containsMatchIn(line)) "${file.name}:${index + 1}: ${line.trim()}" else null
                    }
                }
                .toList()

        assertTrue(
            offenders.isEmpty(),
            "temp folders must come from TempDirs (deleted after each test). Offenders:\n" + offenders.joinToString("\n"),
        )
    }
}
