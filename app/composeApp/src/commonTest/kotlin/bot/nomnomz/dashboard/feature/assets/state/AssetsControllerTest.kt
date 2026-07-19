// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.assets.state

import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.io.AssetFile
import bot.nomnomz.dashboard.core.io.AssetFilePickerIO
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AssetsApi
import bot.nomnomz.dashboard.core.network.ChannelAsset
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_asset_deleted
import nomnomzbot.composeapp.generated.resources.feedback_asset_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_asset_uploaded

// Proves the Assets page state machine the screen renders: surface the channel's real media assets —
// empty when there are none, error if the list call fails — and follow through on every write (upload /
// delete) by re-listing so the consequence is observed (a new row, a removed row), not merely that a
// call happened. Also proves the OBS-facing public URL join and the extension→MIME inference the
// backend validates against.
class AssetsControllerTest {

    @Test
    fun load_surfaces_the_channel_assets_on_success() = runTest {
        val controller =
            controller(
                RecordingAssetsApi(
                    ApiResult.Ok(
                        listOf(
                            ChannelAsset(
                                id = "a1",
                                name = "alert-badge",
                                displayName = "Alert badge",
                                kind = "image",
                                mimeType = "image/png",
                                sizeBytes = 2048L,
                                createdAt = "2026-07-01T12:00:00Z",
                                url = "/api/v1/assets/file/ch1/alert-badge?v=1",
                            )
                        )
                    )
                )
            )

        controller.load()

        val state: AssetsState = controller.state.value
        assertTrue(state is AssetsState.Ready)
        val assets: List<ChannelAsset> = (state as AssetsState.Ready).assets
        assertEquals(1, assets.size)
        val asset: ChannelAsset = assets.first()
        assertEquals("alert-badge", asset.name)
        assertEquals("Alert badge", asset.displayName)
        assertEquals("image", asset.kind)
        assertEquals("image/png", asset.mimeType)
        assertEquals(2048L, asset.sizeBytes)
        assertEquals("/api/v1/assets/file/ch1/alert-badge?v=1", asset.url)
        assertNull(state.actionError)
    }

    @Test
    fun load_is_empty_when_the_channel_has_no_assets() = runTest {
        val controller = controller(RecordingAssetsApi(ApiResult.Ok(emptyList())))

        controller.load()

        assertTrue(controller.state.value is AssetsState.Empty)
    }

    @Test
    fun load_errors_when_the_list_call_fails() = runTest {
        val controller =
            controller(RecordingAssetsApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))))

        controller.load()

        val state: AssetsState = controller.state.value
        assertTrue(state is AssetsState.Error)
        assertEquals("boom", (state as AssetsState.Error).detail)
    }

    @Test
    fun upload_posts_the_slug_and_file_then_reloads_with_the_new_asset() = runTest {
        val assetsApi = RecordingAssetsApi(ApiResult.Ok(emptyList()))
        val feedback = RecordingFeedback()
        val controller = controller(assetsApi, feedback = feedback)
        controller.load()
        assertTrue(controller.state.value is AssetsState.Empty)

        controller.upload(
            fileName = "logo.png",
            bytes = byteArrayOf(1, 2, 3),
            name = "logo",
            displayName = "Channel logo",
        )

        // The api recorded exactly the multipart pieces the controller built — the slug, the label,
        // and the file whose MIME type was inferred from the .png extension.
        assertEquals(1, assetsApi.uploaded.size)
        val upload: RecordedUpload = assetsApi.uploaded.first()
        assertEquals("logo", upload.name)
        assertEquals("Channel logo", upload.displayName)
        assertEquals("logo.png", upload.file.name)
        assertEquals("image/png", upload.file.mimeType)
        assertTrue(upload.file.bytes.contentEquals(byteArrayOf(1, 2, 3)))

        // The post-write reload surfaced the freshly-stored row.
        val state: AssetsState = controller.state.value
        assertTrue(state is AssetsState.Ready)
        assertEquals("logo", (state as AssetsState.Ready).assets.first().name)
        assertNull(state.actionError)

        // And the success was announced with the "uploaded" label.
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_asset_uploaded, feedback.only.label)
    }

    @Test
    fun upload_infers_the_audio_mime_type_from_the_extension() = runTest {
        val assetsApi = RecordingAssetsApi(ApiResult.Ok(emptyList()))
        val controller = controller(assetsApi)
        controller.load()

        controller.upload(fileName = "fanfare.mp3", bytes = byteArrayOf(9), name = "fanfare", displayName = "Fanfare")

        assertEquals("audio/mpeg", assetsApi.uploaded.first().file.mimeType)
    }

    @Test
    fun uploadAsset_picks_a_file_and_uploads_it_under_its_filename_stem() = runTest {
        val assetsApi = RecordingAssetsApi(ApiResult.Ok(emptyList()))
        val controller =
            controller(
                assetsApi,
                picker = FakeAssetPicker(AssetFile("sub-badge.webp", "image/webp", byteArrayOf(7, 7))),
            )
        controller.load()

        controller.uploadAsset()

        // The filename stem becomes both the slug and the initial display name (the Sound twin's rule).
        val upload: RecordedUpload = assetsApi.uploaded.first()
        assertEquals("sub-badge", upload.name)
        assertEquals("sub-badge", upload.displayName)
        assertEquals("image/webp", upload.file.mimeType)
        assertFalse(controller.isUploading.value)
        assertTrue(controller.state.value is AssetsState.Ready)
    }

    @Test
    fun uploadAsset_does_nothing_when_the_picker_is_cancelled() = runTest {
        val assetsApi = RecordingAssetsApi(ApiResult.Ok(emptyList()))
        val controller = controller(assetsApi, picker = FakeAssetPicker(null))
        controller.load()

        controller.uploadAsset()

        assertTrue(assetsApi.uploaded.isEmpty())
        assertFalse(controller.isUploading.value)
        assertTrue(controller.state.value is AssetsState.Empty)
    }

    @Test
    fun delete_removes_the_asset_then_reloads_to_empty() = runTest {
        val assetsApi =
            RecordingAssetsApi(
                ApiResult.Ok(listOf(ChannelAsset(id = "a9", name = "bye", displayName = "Bye", kind = "audio")))
            )
        val feedback = RecordingFeedback()
        val controller = controller(assetsApi, feedback = feedback)
        controller.load()
        assertTrue(controller.state.value is AssetsState.Ready)

        controller.deleteAsset("a9")

        assertEquals(listOf("a9"), assetsApi.deleted)
        // The store is now empty, so the post-delete reload lands on Empty — the row is really gone.
        assertTrue(controller.state.value is AssetsState.Empty)
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_asset_deleted, feedback.only.label)
    }

    @Test
    fun a_failed_write_keeps_the_list_and_surfaces_the_backend_detail() = runTest {
        val assetsApi =
            RecordingAssetsApi(
                ApiResult.Ok(listOf(ChannelAsset(id = "a1", name = "keep-me", displayName = "Keep me", kind = "image"))),
                writeError = ApiError(413, "TOO_LARGE", "Channel storage limit of 64 MB exceeded."),
            )
        val feedback = RecordingFeedback()
        val controller = controller(assetsApi, feedback = feedback)
        controller.load()

        controller.upload(fileName = "big.gif", bytes = byteArrayOf(0), name = "big", displayName = "Big")

        // The list is kept (not blown away) and the backend's friendly message is surfaced on it.
        val state: AssetsState = controller.state.value
        assertTrue(state is AssetsState.Ready)
        assertEquals("keep-me", (state as AssetsState.Ready).assets.first().name)
        assertEquals("Channel storage limit of 64 MB exceeded.", state.actionError)
        // And announced as an ERROR carrying that detail — never a success.
        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(Res.string.feedback_asset_save_failed, feedback.only.label)
        assertEquals(listOf<Any>("Channel storage limit of 64 MB exceeded."), feedback.only.formatArgs)
    }

    @Test
    fun a_failed_delete_surfaces_the_error_over_the_kept_list() = runTest {
        val assetsApi =
            RecordingAssetsApi(
                ApiResult.Ok(listOf(ChannelAsset(id = "a1", name = "keep-me", displayName = "Keep me", kind = "image"))),
                writeError = ApiError(403, "FORBIDDEN", "no permission"),
            )
        val controller = controller(assetsApi)
        controller.load()

        controller.deleteAsset("a1")

        val state: AssetsState = controller.state.value
        assertTrue(state is AssetsState.Ready)
        assertEquals(1, (state as AssetsState.Ready).assets.size)
        assertEquals("no permission", state.actionError)
    }

    @Test
    fun publicUrl_joins_the_active_backend_origin_onto_the_relative_serving_url() = runTest {
        val controller =
            controller(
                RecordingAssetsApi(ApiResult.Ok(emptyList())),
                baseUrl = { "http://localhost:5080/" },
            )

        assertEquals(
            "http://localhost:5080/api/v1/assets/file/ch1/logo?v=3",
            controller.publicUrl("/api/v1/assets/file/ch1/logo?v=3"),
        )
    }

    @Test
    fun publicUrl_is_null_without_an_active_connection() = runTest {
        val controller = controller(RecordingAssetsApi(ApiResult.Ok(emptyList())), baseUrl = { null })

        assertNull(controller.publicUrl("/api/v1/assets/file/ch1/logo?v=3"))
    }

    private fun controller(
        assetsApi: AssetsApi,
        picker: AssetFilePickerIO = FakeAssetPicker(null),
        baseUrl: () -> String? = { null },
        feedback: RecordingFeedback = RecordingFeedback(),
    ): AssetsController =
        AssetsController(
            assetsApi = assetsApi,
            assetPicker = picker,
            baseUrlProvider = baseUrl,
            feedback = feedback,
        )
}

/** One recorded upload call: the slug, label, and the multipart file the controller built. */
private data class RecordedUpload(val name: String, val displayName: String, val file: AssetFile)

// A recording fake that behaves like the backend store: list() returns the live store, and each
// successful write mutates it so the controller's post-write reload observes the real consequence
// (a new row, a replaced row, a removed row) — not merely that a call happened. [writeError] forces
// every write to fail (the store is left untouched) to exercise the error path. A list-level failure
// is modelled by passing a Failure as the initial result.
private class RecordingAssetsApi(
    initial: ApiResult<List<ChannelAsset>>,
    private val writeError: ApiError? = null,
) : AssetsApi {
    private val listFailure: ApiError? = (initial as? ApiResult.Failure)?.error
    private val store: MutableList<ChannelAsset> =
        (initial as? ApiResult.Ok)?.value?.toMutableList() ?: mutableListOf()

    val uploaded: MutableList<RecordedUpload> = mutableListOf()
    val deleted: MutableList<String> = mutableListOf()

    override suspend fun list(): ApiResult<List<ChannelAsset>> =
        listFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(store.toList())

    override suspend fun upload(
        name: String,
        displayName: String,
        file: AssetFile,
    ): ApiResult<ChannelAsset> {
        uploaded += RecordedUpload(name, displayName, file)
        writeError?.let { return ApiResult.Failure(it) }
        // Same name = replace in place, matching the backend's create-or-replace contract.
        store.removeAll { it.name == name }
        val stored: ChannelAsset =
            ChannelAsset(
                id = "asset-${uploaded.size}",
                name = name,
                displayName = displayName,
                kind = if (file.mimeType.startsWith("image/")) "image" else "audio",
                mimeType = file.mimeType,
                sizeBytes = file.bytes.size.toLong(),
                url = "/api/v1/assets/file/ch1/$name?v=${uploaded.size}",
            )
        store += stored
        return ApiResult.Ok(stored)
    }

    override suspend fun delete(id: String): ApiResult<Unit> {
        deleted += id
        writeError?.let { return ApiResult.Failure(it) }
        store.removeAll { it.id == id }
        return ApiResult.Ok(Unit)
    }
}

/** A fake picker that returns a fixed [result] (null = the user cancelled the dialog). */
private class FakeAssetPicker(private val result: AssetFile?) : AssetFilePickerIO {
    override suspend fun pick(): AssetFile? = result
}
