// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.connection

import java.io.File
import java.nio.file.Files
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlinx.coroutines.test.runTest

// Proves S111b: saving, switching, and forgetting desktop connections persists correctly and keeps
// each connection's token isolated in the vault.
class SavedConnectionsRepositoryTest {

    private fun tempStateFile(): File = File(Files.createTempDirectory("saved-connections-test").toFile(), "saved-connections.json")

    private fun repository(stateFile: File, tokenDir: File): SavedConnectionsRepository =
        SavedConnectionsRepository(FileSavedConnectionsStore(stateFile), TokenVault(tokenDir))

    @Test
    fun `switching then forgetting one connection leaves the other intact and falls back correctly`() = runTest {
        val stateFile: File = tempStateFile()
        val tokenDir: File = Files.createTempDirectory("saved-connections-tokens").toFile()

        val connectionA = SavedConnection(id = "conn-a", label = "Home", baseUrl = "http://localhost:5080", lastUsedAt = null)
        val connectionB = SavedConnection(id = "conn-b", label = "LAN", baseUrl = "http://192.168.2.60:5080", lastUsedAt = null)

        val repo: SavedConnectionsRepository = repository(stateFile, tokenDir)
        repo.add(connectionA)
        repo.add(connectionB)
        repo.switchTo("conn-b")
        repo.forget("conn-b")

        // Reload through a FRESH instance to prove the state was actually persisted, not just held in memory.
        val reloaded: SavedConnectionsRepository = repository(stateFile, tokenDir)
        val remaining: List<SavedConnection> = reloaded.list()

        assertEquals(listOf("conn-a"), remaining.map { it.id })
        assertEquals("conn-a", reloaded.activeId())
    }

    @Test
    fun `forgetting a connection deletes its token but leaves the other connection's token readable`() = runTest {
        val stateFile: File = tempStateFile()
        val tokenDir: File = Files.createTempDirectory("saved-connections-tokens").toFile()
        val tokenVault = TokenVault(tokenDir)

        val connectionA = SavedConnection(id = "conn-a", label = "Home", baseUrl = "http://localhost:5080", lastUsedAt = null)
        val connectionB = SavedConnection(id = "conn-b", label = "LAN", baseUrl = "http://192.168.2.60:5080", lastUsedAt = null)

        tokenVault.write("conn-a", SessionTokens(accessToken = "token-a"))
        tokenVault.write("conn-b", SessionTokens(accessToken = "token-b"))

        val repo = SavedConnectionsRepository(FileSavedConnectionsStore(stateFile), tokenVault)
        repo.add(connectionA)
        repo.add(connectionB)

        repo.forget("conn-b")

        assertNull(tokenVault.read("conn-b"))
        assertEquals("token-a", tokenVault.read("conn-a")?.accessToken)
    }

    @Test
    fun `the store round-trips through a real file in a temp dir across fresh instances`() = runTest {
        val stateFile: File = tempStateFile()
        val store: SavedConnectionsStore = FileSavedConnectionsStore(stateFile)

        store.add(SavedConnection(id = "conn-a", label = "Home", baseUrl = "http://localhost:5080", lastUsedAt = null))
        store.setActive("conn-a")

        val reloaded: SavedConnectionsStore = FileSavedConnectionsStore(stateFile)
        assertEquals(listOf("conn-a"), reloaded.list().map { it.id })
        assertEquals("conn-a", reloaded.activeId())
    }
}
