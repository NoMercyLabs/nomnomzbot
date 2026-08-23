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
import kotlin.test.assertFalse
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.Json

// Proves S111a: the desktop vault never writes a token value in the clear, and a legacy
// plaintext vault is migrated transparently on first read.
class TokenVaultEncryptionTest {

    private val secretToken = "super-secret-access-token-value-12345"

    private fun tempDir(): File = Files.createTempDirectory("token-vault-test").toFile()

    private fun assertNoFileContainsSecret(dir: File, secret: String) {
        val offenders: List<File> =
            dir.walkTopDown().filter { it.isFile }.filter { file ->
                val bytes: ByteArray = file.readBytes()
                val text = String(bytes, Charsets.ISO_8859_1)
                text.contains(secret)
            }.toList()
        assertTrue(offenders.isEmpty(), "token leaked in plaintext into: ${offenders.map { it.path }}")
    }

    @Test
    fun `write then read round-trips the token without ever storing it in plaintext`() = runTest {
        val dir: File = tempDir()
        val vault = TokenVault(dir)

        vault.write("profile-1", SessionTokens(accessToken = secretToken, refreshToken = "refresh-abc"))

        assertNoFileContainsSecret(dir, secretToken)
        assertNoFileContainsSecret(dir, "refresh-abc")

        val readBack: SessionTokens? = vault.read("profile-1")
        assertEquals(secretToken, readBack?.accessToken)
        assertEquals("refresh-abc", readBack?.refreshToken)
    }

    @Test
    fun `a pre-seeded plaintext vault is migrated to encrypted form on first read`() = runTest {
        val dir: File = tempDir()
        dir.mkdirs()
        val plaintextFile = File(dir, "profile-legacy.json")
        val json = Json { ignoreUnknownKeys = true }
        val plaintextTokens = SessionTokens(accessToken = secretToken, refreshToken = "refresh-legacy")
        plaintextFile.writeText(json.encodeToString(SessionTokens.serializer(), plaintextTokens))

        val vault = TokenVault(dir)
        val readBack: SessionTokens? = vault.read("profile-legacy")

        assertEquals(secretToken, readBack?.accessToken)
        assertEquals("refresh-legacy", readBack?.refreshToken)

        // The plaintext file must be gone (or at least no longer contain the secret) after migration.
        if (plaintextFile.exists()) {
            assertFalse(plaintextFile.readText().contains(secretToken))
        }
        assertNoFileContainsSecret(dir, secretToken)
    }

    @Test
    fun `a corrupt vault file fails closed instead of crashing`() = runTest {
        val dir: File = tempDir()
        dir.mkdirs()
        File(dir, "profile-corrupt.json.enc").writeBytes(byteArrayOf(1, 2, 3, 4, 5))

        val vault = TokenVault(dir)
        val readBack: SessionTokens? = vault.read("profile-corrupt")

        assertEquals(null, readBack)
    }
}
