// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import io.ktor.http.ContentType
import kotlinx.serialization.Serializable

// The typed local-bundles facade — the channel's portable content packs (bundles.md §5): export a hand-picked
// set of the channel's own content (commands, pipelines, widgets, sounds) as a portable ZIP, inspect a ZIP
// before install (its manifest, the capabilities it asks for, and any blocking issues), install it under a
// conflict policy, list what is installed, and uninstall a pack. Real data only — the backend builds the ZIP
// from the channel's actual content and reads the manifest out of the uploaded file; nothing is fabricated. The
// state holder depends on this interface and fakes it in tests without HTTP.
//
// A command's linked pipeline is auto-included server-side when the command is exported, so the pick-list only
// needs the four top-level content types.
//
// Backend routes (BundlesController), all channel-scoped:
//   POST   /api/v1/channels/{channelId}/bundles/export             →  application/zip (a portable pack)
//   POST   /api/v1/channels/{channelId}/bundles/inspect            →  StatusResponseDto<BundleInspection>
//   POST   /api/v1/channels/{channelId}/bundles/import             →  StatusResponseDto<InstalledBundle>
//   GET    /api/v1/channels/{channelId}/bundles/installed          →  StatusResponseDto<List<InstalledBundle>>
//   DELETE /api/v1/channels/{channelId}/bundles/installed/{id}     →  204 No Content
interface BundlesApi {
    /** Build a portable ZIP from the picked [body] items + metadata. Returns the raw ZIP bytes for the save dialog. */
    suspend fun export(channelId: String, body: ExportBody): ApiResult<ByteArray>

    /**
     * Inspect an uploaded bundle ZIP WITHOUT installing it — reads its manifest, the capabilities it requests, and
     * any blocking issues, so the import wizard can show what the pack contains and block a bad one before install.
     */
    suspend fun inspect(channelId: String, fileName: String, bytes: ByteArray): ApiResult<BundleInspection>

    /**
     * Install an uploaded bundle ZIP under a conflict [policy] (rename / overwrite / skip — see [ImportPolicy]).
     * Returns the resulting [InstalledBundle] row; the caller re-lists after a successful install.
     */
    suspend fun import(
        channelId: String,
        fileName: String,
        fileBytes: ByteArray,
        policy: String,
    ): ApiResult<InstalledBundle>

    /** The bundles currently installed on the channel (local imports + marketplace installs). */
    suspend fun installed(channelId: String): ApiResult<List<InstalledBundle>>

    /** Uninstall an installed bundle by its [id] — removes exactly what that pack created. */
    suspend fun uninstall(channelId: String, id: String): ApiResult<Unit>
}

class RestBundlesApi(private val client: ApiClient) : BundlesApi {
    // A JSON pick-list in, a streamed ZIP out — postBytesWithBody sends the body as JSON and reads the raw bytes.
    override suspend fun export(channelId: String, body: ExportBody): ApiResult<ByteArray> =
        client.postBytesWithBody("api/v1/channels/$channelId/bundles/export", body)

    override suspend fun inspect(
        channelId: String,
        fileName: String,
        bytes: ByteArray,
    ): ApiResult<BundleInspection> =
        client.postMultipartFile(
            "api/v1/channels/$channelId/bundles/inspect",
            "file",
            fileName,
            bytes,
            ContentType.parse("application/zip"),
        )

    override suspend fun import(
        channelId: String,
        fileName: String,
        fileBytes: ByteArray,
        policy: String,
    ): ApiResult<InstalledBundle> =
        client.postMultipartWithFields(
            "api/v1/channels/$channelId/bundles/import",
            fileFieldName = "file",
            fileName = fileName,
            fileBytes = fileBytes,
            fileContentType = ContentType.parse("application/zip"),
            fields = mapOf("policy" to policy),
        )

    // The installed list is a StatusResponseDto whose data IS the list (a single-value envelope wrapping an array),
    // so it is read with getEnvelope — not the flat PaginatedResponse getDirect the reward/command lists use.
    override suspend fun installed(channelId: String): ApiResult<List<InstalledBundle>> =
        client.getEnvelope("api/v1/channels/$channelId/bundles/installed")

    override suspend fun uninstall(channelId: String, id: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/bundles/installed/$id")
}

/**
 * The conflict policy the import applies when a bundle's content collides with something already on the channel:
 * [Rename] keeps both (the incoming copy is renamed), [Overwrite] replaces the existing item, [Skip] leaves the
 * existing item untouched and drops the incoming one. [all] is the ordered set the import selector offers.
 */
object ImportPolicy {
    const val Rename: String = "rename"
    const val Overwrite: String = "overwrite"
    const val Skip: String = "skip"

    val all: List<String> = listOf(Rename, Overwrite, Skip)
}

/**
 * One item to export (backend `ExportItemRef`): the content [type] (`command` / `pipeline` / `widget` / `sound`)
 * and the item's [id]. A command's linked pipeline is pulled in automatically server-side, so it need not be
 * listed separately.
 */
@Serializable
data class ExportItemRef(val type: String, val id: String)

/**
 * The metadata stamped onto an exported bundle's manifest (backend `BundleMetadata`). [name] and [version] are
 * required; [author] / [license] / [description] are optional provenance the pack carries for the marketplace and
 * the import wizard to show.
 */
@Serializable
data class BundleMetadataBody(
    val name: String,
    val version: String,
    val author: String? = null,
    val license: String? = null,
    val description: String? = null,
)

/** The export request body (backend `ExportRequest`): the picked [items] plus the bundle [metadata]. */
@Serializable
data class ExportBody(val items: List<ExportItemRef>, val metadata: BundleMetadataBody)

/**
 * One entry in a bundle's manifest as read back by inspect (backend `BundleManifestItem`): its content [type] and
 * [name], the [path] inside the ZIP, and the other manifest item ids it [dependencies] on. Defaults everywhere so
 * a partial/older manifest still deserializes.
 */
@Serializable
data class BundleManifestItem(
    val type: String = "",
    val name: String = "",
    val path: String = "",
    val dependencies: List<String> = emptyList(),
)

/**
 * A bundle's manifest as read by inspect (backend `BundleManifest`): its [schemaVersion], the stamped [metadata],
 * and the [items] it contains. Defaults everywhere so an empty/partial manifest still deserializes to a shown row.
 */
@Serializable
data class BundleManifest(
    val schemaVersion: Int = 0,
    val metadata: BundleMetadataBody = BundleMetadataBody(name = "", version = ""),
    val items: List<BundleManifestItem> = emptyList(),
)

/**
 * The result of inspecting an uploaded bundle ZIP (backend `BundleInspection`): its [manifest], the
 * [capabilities] it asks for (e.g. runs custom code), and any blocking [issues] the import wizard must surface —
 * a non-empty [issues] disables the install. All defaulted so a lenient/partial response still renders.
 */
@Serializable
data class BundleInspection(
    val manifest: BundleManifest = BundleManifest(),
    val capabilities: List<String> = emptyList(),
    val issues: List<String> = emptyList(),
)

/**
 * An installed bundle row (backend `InstalledBundle`): its [id], display [name], [source] (`local` for a
 * hand-imported ZIP, `marketplace` for a hosted install), the [marketplaceItemId] it came from when hosted, its
 * [version], and the [installedAt] timestamp. Defaults everywhere; the backend serializes it as an untyped object.
 */
@Serializable
data class InstalledBundle(
    val id: String = "",
    val name: String = "",
    val source: String = "",
    val marketplaceItemId: String? = null,
    val version: String = "",
    val installedAt: String = "",
)
