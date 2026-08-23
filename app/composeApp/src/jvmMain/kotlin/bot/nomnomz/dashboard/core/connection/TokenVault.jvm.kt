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

import bot.nomnomz.dashboard.core.platform.DesktopDataDir
import java.io.File
import java.nio.file.Files
import java.nio.file.attribute.PosixFilePermission
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json

// Desktop token custody — a per-profile AES-256-GCM encrypted file under the OS app-data dir
// (frontend.md §6, S111a). The encryption key is a machine-scoped random key kept in a sibling
// file outside the vault directory, permission-locked to the owner (POSIX 0600) where the
// filesystem supports it; on filesystems without POSIX permissions (NTFS on desktop Windows) the
// key file relies on the user-profile ACL that already restricts LOCALAPPDATA to the owning
// account. This is a from-scratch fallback, not OS DPAPI/Keychain — no new dependency (e.g. JNA
// for CryptProtectData, or a keychain binding) was added to reach the OS-native vault; that
// remains a follow-up if a heavier dependency is approved.
//
// A legacy plaintext vault (pre-S111a) is transparently migrated to the encrypted form on first
// read, then deleted. A corrupt/undecryptable vault entry fails closed (treated as "no tokens")
// rather than crashing. No token value is ever written to a log.
actual class TokenVault internal constructor(private val dir: File) : SessionTokenStore {

    actual constructor() : this(defaultDir())

    private val json: Json = Json { ignoreUnknownKeys = true }

    init {
        dir.mkdirs()
    }

    private val keyFile: File by lazy { File(dir.parentFile ?: dir, "vault.key") }

    private fun secretKey(): SecretKeySpec {
        if (!keyFile.exists()) {
            val generated = ByteArray(KEY_LENGTH_BYTES)
            SecureRandom().nextBytes(generated)
            keyFile.parentFile?.mkdirs()
            keyFile.writeBytes(generated)
            lockDownPermissions(keyFile)
        }
        val keyBytes: ByteArray = keyFile.readBytes()
        return SecretKeySpec(keyBytes, "AES")
    }

    private fun lockDownPermissions(file: File) {
        runCatching {
            val permissions: MutableSet<PosixFilePermission> =
                mutableSetOf(PosixFilePermission.OWNER_READ, PosixFilePermission.OWNER_WRITE)
            Files.setPosixFilePermissions(file.toPath(), permissions)
        }
        // Non-POSIX filesystems (NTFS) silently keep the default ACL — no crash, no fallback needed.
    }

    private fun encryptedFileFor(profileId: String): File = File(dir, "$profileId.json.enc")

    private fun legacyPlaintextFileFor(profileId: String): File = File(dir, "$profileId.json")

    private fun encrypt(plaintext: ByteArray): ByteArray {
        val iv = ByteArray(GCM_IV_LENGTH_BYTES)
        SecureRandom().nextBytes(iv)
        val cipher: Cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, secretKey(), GCMParameterSpec(GCM_TAG_LENGTH_BITS, iv))
        val ciphertext: ByteArray = cipher.doFinal(plaintext)
        return iv + ciphertext
    }

    private fun decrypt(payload: ByteArray): ByteArray? =
        runCatching {
            require(payload.size > GCM_IV_LENGTH_BYTES) { "payload too short" }
            val iv: ByteArray = payload.copyOfRange(0, GCM_IV_LENGTH_BYTES)
            val ciphertext: ByteArray = payload.copyOfRange(GCM_IV_LENGTH_BYTES, payload.size)
            val cipher: Cipher = Cipher.getInstance(TRANSFORMATION)
            cipher.init(Cipher.DECRYPT_MODE, secretKey(), GCMParameterSpec(GCM_TAG_LENGTH_BITS, iv))
            cipher.doFinal(ciphertext)
        }.getOrNull()

    private fun migrateLegacyPlaintext(profileId: String): SessionTokens? {
        val legacyFile: File = legacyPlaintextFileFor(profileId)
        if (!legacyFile.exists()) return null
        val tokens: SessionTokens =
            runCatching { json.decodeFromString(SessionTokens.serializer(), legacyFile.readText()) }
                .getOrNull() ?: run {
                // Unreadable legacy file — remove it rather than leave a dangling plaintext artifact.
                legacyFile.delete()
                return null
            }
        writeEncrypted(profileId, tokens)
        legacyFile.delete()
        return tokens
    }

    private fun writeEncrypted(profileId: String, tokens: SessionTokens) {
        val plaintext: ByteArray = json.encodeToString(SessionTokens.serializer(), tokens).toByteArray(Charsets.UTF_8)
        encryptedFileFor(profileId).writeBytes(encrypt(plaintext))
    }

    actual override suspend fun read(profileId: String): SessionTokens? =
        withContext(Dispatchers.IO) {
            val encryptedFile: File = encryptedFileFor(profileId)
            if (!encryptedFile.exists()) {
                return@withContext migrateLegacyPlaintext(profileId)
            }
            val decrypted: ByteArray = decrypt(encryptedFile.readBytes()) ?: return@withContext null
            runCatching { json.decodeFromString(SessionTokens.serializer(), decrypted.toString(Charsets.UTF_8)) }
                .getOrNull()
        }

    actual override suspend fun write(profileId: String, tokens: SessionTokens) {
        withContext(Dispatchers.IO) { writeEncrypted(profileId, tokens) }
    }

    actual override suspend fun clear(profileId: String) {
        withContext(Dispatchers.IO) {
            encryptedFileFor(profileId).delete()
            legacyPlaintextFileFor(profileId).delete()
        }
    }

    private companion object {
        const val TRANSFORMATION: String = "AES/GCM/NoPadding"
        const val KEY_LENGTH_BYTES: Int = 32
        const val GCM_IV_LENGTH_BYTES: Int = 12
        const val GCM_TAG_LENGTH_BITS: Int = 128

        fun defaultDir(): File = File(DesktopDataDir.resolve(), "tokens")
    }
}
