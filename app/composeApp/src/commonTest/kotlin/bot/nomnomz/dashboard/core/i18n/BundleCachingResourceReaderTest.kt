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

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.runTest
import org.jetbrains.compose.resources.ExperimentalResourceApi
import org.jetbrains.compose.resources.ResourceReader
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals

// Proves the fix for the boot refetch storm: a locale's packed string bundle (`.cvr`) is read from its backing
// file ONCE per session, and every `stringResource` byte-range read (`readPart`) is served by slicing the
// cached bundle — never re-fetching the whole file per string. The value of the fix is entirely in "how many
// times does the delegate actually read the file", so that is what these assert, alongside the slices being
// byte-identical to a direct range read (correctness must not be traded for the caching).
@OptIn(ExperimentalResourceApi::class, ExperimentalCoroutinesApi::class)
class BundleCachingResourceReaderTest {

    // A packed "bundle" whose distinct byte ranges stand in for individual strings inside a real `.cvr`.
    private val bundle: ByteArray = "ABCDEFGHIJ".encodeToByteArray()
    private val path: String = "composeResources/values-nl/strings.commonMain.cvr"

    @Test
    fun many_reads_of_one_bundle_hit_the_backing_file_exactly_once() = runTest {
        val delegate = CountingResourceReader(mapOf(path to bundle))
        val reader = BundleCachingResourceReader(delegate)

        // Thirty distinct strings on the boot screens = thirty range reads of the SAME bundle (the exact
        // symptom that produced ~30 identical network GETs before this fix).
        repeat(30) { index ->
            reader.readPart(path, offset = index % bundle.size.toLong(), size = 1)
        }

        // The whole point: one file read backs all thirty range reads.
        assertEquals(1, delegate.readCount(path))
    }

    @Test
    fun concurrent_first_reads_do_not_each_fetch_the_bundle() = runTest {
        // The boot reality: ~30 range reads fire concurrently before the first fetch completes. Without
        // per-bundle deduping each would start its own fetch; the gate below holds the fetch open until all
        // readers have piled onto the same in-flight load, proving they share ONE fetch, not thirty.
        val gate = CompletableDeferred<Unit>()
        val delegate = CountingResourceReader(mapOf(path to bundle), readGate = gate)
        val reader = BundleCachingResourceReader(delegate)
        val dispatcher = UnconfinedTestDispatcher(testScheduler)

        val reads = (0 until 30).map { index ->
            async(dispatcher) { reader.readPart(path, offset = (index % bundle.size).toLong(), size = 1) }
        }
        // Every reader is now parked on the single in-flight fetch; release it and let them all resolve.
        gate.complete(Unit)
        reads.awaitAll()

        assertEquals(1, delegate.readCount(path))
    }

    @Test
    fun read_part_returns_exactly_the_requested_byte_range() = runTest {
        val delegate = CountingResourceReader(mapOf(path to bundle))
        val reader = BundleCachingResourceReader(delegate)

        // Non-adjacent ranges, out of order, all sliced from the one cached bundle.
        assertContentEquals("CDE".encodeToByteArray(), reader.readPart(path, offset = 2, size = 3))
        assertContentEquals("A".encodeToByteArray(), reader.readPart(path, offset = 0, size = 1))
        assertContentEquals("HIJ".encodeToByteArray(), reader.readPart(path, offset = 7, size = 3))
        assertEquals(1, delegate.readCount(path))
    }

    @Test
    fun different_bundles_are_cached_independently() = runTest {
        val enPath: String = "composeResources/values/strings.commonMain.cvr"
        val enBundle: ByteArray = "0123456789".encodeToByteArray()
        val delegate = CountingResourceReader(mapOf(path to bundle, enPath to enBundle))
        val reader = BundleCachingResourceReader(delegate)

        reader.readPart(path, offset = 1, size = 2)
        reader.readPart(enPath, offset = 3, size = 2)
        reader.readPart(path, offset = 4, size = 2)
        reader.readPart(enPath, offset = 0, size = 1)

        // Each locale's bundle loads once; neither read count leaks into the other.
        assertEquals(1, delegate.readCount(path))
        assertEquals(1, delegate.readCount(enPath))
    }

    @Test
    fun read_and_get_uri_pass_through_to_the_delegate() = runTest {
        val delegate = CountingResourceReader(mapOf(path to bundle))
        val reader = BundleCachingResourceReader(delegate)

        // Full-file reads (images/fonts/raw files) are delegated untouched — they already have their own
        // decoded caches, so this decorator must not shadow them with a byte cache.
        assertContentEquals(bundle, reader.read(path))
        assertEquals("uri:$path", reader.getUri(path))
    }
}

// A [ResourceReader] over an in-memory file map that counts how often each path's bytes are actually read, so a
// test can assert the caching decorator collapses N range reads into ONE backing read. An optional [readGate]
// suspends every `read` until completed, letting a test pile concurrent readers onto one in-flight load.
@OptIn(ExperimentalResourceApi::class)
private class CountingResourceReader(
    private val files: Map<String, ByteArray>,
    private val readGate: CompletableDeferred<Unit>? = null,
) : ResourceReader {
    private val reads: MutableMap<String, Int> = mutableMapOf()

    fun readCount(path: String): Int = reads[path] ?: 0

    override suspend fun read(path: String): ByteArray {
        reads[path] = (reads[path] ?: 0) + 1
        readGate?.await()
        return files[path] ?: error("no such fake resource: $path")
    }

    override suspend fun readPart(path: String, offset: Long, size: Long): ByteArray {
        val from: Int = offset.toInt()
        return read(path).copyOfRange(from, from + size.toInt())
    }

    override fun getUri(path: String): String = "uri:$path"
}
