// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.i18n

import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import org.jetbrains.compose.resources.ExperimentalResourceApi
import org.jetbrains.compose.resources.ResourceReader

// A [ResourceReader] decorator that reads each Compose Resources bundle FILE at most once per session and
// serves every byte-range read from the cached bytes. It is provided over `LocalResourceReader` for the whole
// app (see App.kt) so all `stringResource` reads route through it.
//
// Why this exists — the boot refetch storm:
// Compose Multiplatform stores all of a locale's strings in a single packed bundle (`values-nl/
// strings.commonMain.cvr`); each string is a byte range `(offset, size)` inside it, read via
// [ResourceReader.readPart]. CMP's only string cache (`stringItemsCache`) is keyed per string
// (`path/offset-size`), and on web `readPart` fetches the ENTIRE bundle file per call with no file-level byte
// cache — so the ~30 distinct strings shown on the boot screens each issued their own network GET for the same
// `.cvr` (observed live: ~30 identical `[304]` requests in a row), materially slowing boot. CMP's own web
// caching (the Web Cache API path) dedupes whole-response reads per path but does not cover `readPart`
// byte-range reads in 1.9.0, so the storm survives it.
//
// The fix — cache the WHOLE bundle once, slice locally:
// [readPart] loads the full bundle bytes for a `path` a single time (deduping concurrent first-reads so the
// ~30 simultaneous boot reads share ONE fetch, not 30) and returns the requested slice from memory. The bundle
// is keyed by `path`, so each locale's `.cvr` (`values/…`, `values-nl/…`) loads at most once per session and a
// later language switch re-reads a previously-loaded locale from cache with zero network round-trips. Bundles
// are small (the packed string table), so holding them costs little. [read] and [getUri] are pass-through:
// full-file reads (images, fonts, raw files) already have CMP's decoded caches and are left untouched.
@OptIn(ExperimentalResourceApi::class)
class BundleCachingResourceReader(private val delegate: ResourceReader) : ResourceReader {

    // Guards [bundles] so the first reader of a path wins the fetch and the rest await the same in-flight load
    // (mirrors CMP's own `AsyncCache`): parallel loads of DIFFERENT bundles proceed, duplicate loads of the
    // SAME bundle are serialized onto one fetch. A cancelled load is dropped so the next read retries it.
    private val mutex = Mutex()
    private val bundles = mutableMapOf<String, Deferred<ByteArray>>()

    override suspend fun readPart(path: String, offset: Long, size: Long): ByteArray {
        val bytes: ByteArray = fullBundle(path)
        val from: Int = offset.toInt()
        return bytes.copyOfRange(from, from + size.toInt())
    }

    override suspend fun read(path: String): ByteArray = delegate.read(path)

    override fun getUri(path: String): String = delegate.getUri(path)

    private suspend fun fullBundle(path: String): ByteArray = coroutineScope {
        val load: Deferred<ByteArray> =
            mutex.withLock {
                val existing: Deferred<ByteArray>? = bundles[path]
                if (existing != null && !existing.isCancelled) {
                    existing
                } else {
                    // LAZY so the fetch starts on `await`, after the mutex is released.
                    val started: Deferred<ByteArray> =
                        async(start = CoroutineStart.LAZY) { delegate.read(path) }
                    bundles[path] = started
                    started
                }
            }
        load.await()
    }
}
