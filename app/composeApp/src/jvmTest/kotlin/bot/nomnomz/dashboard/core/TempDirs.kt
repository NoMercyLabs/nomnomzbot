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
import java.nio.file.Files

// The only place a jvmTest may create a temp folder. A test holds one instance and calls
// [deleteAll] from an @AfterTest, so nothing is left in the OS temp dir, pass or fail.
// TempDirsGuardTest fails the build when a test creates temp folders any other way.
class TempDirs {

    private val created: MutableList<File> = mutableListOf()

    fun create(prefix: String): File {
        val dir: File = Files.createTempDirectory(prefix).toFile()
        created.add(dir)
        return dir
    }

    fun deleteAll() {
        created.forEach { it.deleteRecursively() }
        created.clear()
    }
}
