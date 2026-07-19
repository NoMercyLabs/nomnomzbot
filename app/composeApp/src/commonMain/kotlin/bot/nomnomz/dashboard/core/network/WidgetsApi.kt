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

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject

// The typed widgets facade — the channel's OBS browser-source overlays the Overlays page renders. Widgets are
// compile-on-save: authored source is compiled into an append-only version the overlay serves + hot-reloads, so
// this facade mirrors CodeScriptsApi's versioned-resource shape (compile ≈ createVersion, versions ≈
// listVersions, rollback ≈ publishVersion). Real data only: the backend lists the channel's stored widgets, each
// carrying its live browser-source URL (the value the operator pastes into OBS). The state holder depends on this
// interface and fakes it in tests without HTTP.
//
// Backend routes (WidgetsController, all under /api/v1/channels/{channelId}/widgets):
//   GET    .../widgets                              →  PaginatedResponse<WidgetDetail>          (the channel's overlays)
//   POST   .../widgets                              →  StatusResponseDto<WidgetDetail>          (create — { name, framework })
//   PUT    .../widgets/{widgetId}                   →  StatusResponseDto<WidgetDetail>          (partial update — name / isEnabled)
//   DELETE .../widgets/{widgetId}                   →  204 No Content                           (removes the overlay; its URL dies)
//   GET    .../widgets/templates                    →  StatusResponseDto<WidgetTemplate[]>      (starter templates for the editor)
//   POST   .../widgets/clone                        →  StatusResponseDto<WidgetDetail>          (fork an installed widget to a custom copy)
//   POST   .../widgets/{widgetId}/compile           →  StatusResponseDto<WidgetVersionDetail>   (compile-on-save → next version; always 200)
//   GET    .../widgets/{widgetId}/versions          →  PaginatedResponse<WidgetVersionSummary>  (version history, newest first)
//   GET    .../widgets/{widgetId}/versions/{vid}    →  StatusResponseDto<WidgetVersionDetail>   (a version's full source + build log)
//   POST   .../widgets/{widgetId}/rollback/{vid}    →  StatusResponseDto<WidgetDetail>          (re-serve a past version)
interface WidgetsApi {
    /** The channel's overlay widgets — each with its OBS browser-source URL. */
    suspend fun list(channelId: String): ApiResult<List<WidgetSummary>>

    /**
     * Flip a widget's enabled flag via the partial-update endpoint (no dedicated toggle route). Only
     * [enabled] is sent; every other field stays null and the backend leaves it untouched.
     */
    suspend fun setEnabled(channelId: String, widgetId: String, enabled: Boolean): ApiResult<Unit>

    /**
     * Delete a widget, addressed by its [widgetId]. Destructive: the overlay's browser-source URL stops
     * resolving once it is gone, so the screen confirms before calling this.
     */
    suspend fun delete(channelId: String, widgetId: String): ApiResult<Unit>

    /** Create a new widget with [body] ({ name, framework }). Returns the created row (including its [WidgetSummary.id]). */
    suspend fun create(channelId: String, body: CreateWidgetBody): ApiResult<WidgetSummary>

    /** Rename a widget — sends a partial PUT with only [name]. */
    suspend fun rename(channelId: String, widgetId: String, name: String): ApiResult<Unit>

    /**
     * Persist a widget's runtime [settings] — a partial PUT carrying only the `settings` object (the backend
     * `UpdateWidgetRequest.Settings`). The overlay reads this config at render time; a typed per-widget-type form
     * (e.g. the `chat_box` font/background knobs) writes the whole object here. Other fields stay untouched.
     */
    suspend fun updateSettings(channelId: String, widgetId: String, settings: JsonObject): ApiResult<Unit>

    /**
     * The widget's typed settings schema — the field/type/default contract the dashboard renders its generic
     * settings form from (so a first-party widget is configured through controls, not by editing its source).
     * Fails for a self-authored custom widget with no first-party schema (those use the code editor).
     */
    suspend fun getSettingsSchema(channelId: String, widgetId: String): ApiResult<WidgetSettingsSchemaDto>

    /**
     * Compile-on-save: send the authored [sourceCode] to be built into the widget's next append-only version.
     * Always resolves 200 with a [WidgetVersionDetail] whose `buildStatus` is `pending` / `success` / `error`
     * (a failed build keeps the last good version live). The overlay hot-reloads itself on success.
     */
    suspend fun compile(channelId: String, widgetId: String, sourceCode: String): ApiResult<WidgetVersionDetail>

    /** The widget's version history, newest first (for the rollback / debug list). */
    suspend fun listVersions(channelId: String, widgetId: String): ApiResult<List<WidgetVersionSummary>>

    /** One version in full — carries the [WidgetVersionDetail.sourceCode] the editor loads to edit. */
    suspend fun getVersion(channelId: String, widgetId: String, versionId: String): ApiResult<WidgetVersionDetail>

    /** Roll the overlay back to a past [versionId] (it becomes the served version again). Returns the widget. */
    suspend fun rollback(channelId: String, widgetId: String, versionId: String): ApiResult<WidgetSummary>

    /**
     * Load the widget's multi-file project — its `src/` file set + manifest — for the editor to open
     * (`GET .../widgets/{widgetId}/project`). The backend always projects a coherent project (a single-file
     * widget comes back as its one-file scaffold), so this is the editor's authoritative load path.
     */
    suspend fun getProject(channelId: String, widgetId: String): ApiResult<ProjectDto>

    /**
     * Save the widget's multi-file [project]: the server re-builds it (the trust boundary) and, on a clean build,
     * appends + serves a new active version — returning that [WidgetVersionDetail]. A failed build returns an
     * [ApiResult.Failure] carrying the reason and persists nothing (append-only history records only real saves).
     */
    suspend fun putProject(channelId: String, widgetId: String, project: ProjectDto): ApiResult<WidgetVersionDetail>

    /** The starter templates the create flow offers — each a working, SDK-using source to seed the editor. */
    suspend fun listTemplates(channelId: String): ApiResult<List<WidgetTemplate>>

    /**
     * Clone an installed widget into a NEW, fully-owned `custom` widget (source copied in + compiled), so a
     * first-party / gallery widget becomes independently editable. Addressed by the source [installedWidgetId].
     */
    suspend fun clone(channelId: String, installedWidgetId: String): ApiResult<WidgetSummary>

    /**
     * Install a verified gallery widget into this channel by its [galleryItemId]: the backend copies the item's
     * source in, compiles it into v1, and the overlay goes live immediately. Returns the newly-installed widget.
     */
    suspend fun install(channelId: String, galleryItemId: String): ApiResult<WidgetSummary>

    /**
     * Clone a GALLERY item into a NEW, fully-owned `custom` widget (source copied in + compiled) so a verified
     * gallery widget becomes independently editable — the gallery counterpart of [clone] (which forks an
     * already-installed widget). Addressed by the source [galleryItemId].
     */
    suspend fun cloneFromGallery(channelId: String, galleryItemId: String): ApiResult<WidgetSummary>
}

class RestWidgetsApi(private val client: ApiClient) : WidgetsApi {
    override suspend fun list(channelId: String): ApiResult<List<WidgetSummary>> {
        // The list is a PaginatedResponse (a flat `{ data: [...] }`), not a StatusResponseDto, so it is read
        // with getDirect (whole-body deserialize) rather than getEnvelope's `data: T` unwrap — same shape as
        // the commands / channels lists.
        return when (
            val page: ApiResult<PaginatedEnvelope<WidgetSummary>> =
                client.getDirect("api/v1/channels/$channelId/widgets?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }

    // The update response is a `StatusResponseDto<WidgetDetail>`, but the controller re-fetches the list after
    // every write, so the body is irrelevant here — any 2xx is success.
    override suspend fun setEnabled(
        channelId: String,
        widgetId: String,
        enabled: Boolean,
    ): ApiResult<Unit> =
        client.putUnit(
            "api/v1/channels/$channelId/widgets/$widgetId",
            UpdateWidgetBody(isEnabled = enabled),
        )

    override suspend fun delete(channelId: String, widgetId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/widgets/$widgetId")

    // POST to the list endpoint; backend returns StatusResponseDto<WidgetDetail> — postEnvelope unwraps `.data`.
    override suspend fun create(channelId: String, body: CreateWidgetBody): ApiResult<WidgetSummary> =
        client.postEnvelope("api/v1/channels/$channelId/widgets", body)

    // Partial PUT — only the name field changes; isEnabled / settings / eventSubscriptions stay as-is.
    override suspend fun rename(channelId: String, widgetId: String, name: String): ApiResult<Unit> =
        client.putUnit("api/v1/channels/$channelId/widgets/$widgetId", UpdateWidgetBody(name = name))

    // Partial PUT — only the settings object changes; name / isEnabled / eventSubscriptions stay as-is.
    override suspend fun updateSettings(
        channelId: String,
        widgetId: String,
        settings: JsonObject,
    ): ApiResult<Unit> =
        client.putUnit(
            "api/v1/channels/$channelId/widgets/$widgetId",
            UpdateWidgetBody(settings = settings),
        )

    override suspend fun getSettingsSchema(
        channelId: String,
        widgetId: String,
    ): ApiResult<WidgetSettingsSchemaDto> =
        client.getEnvelope("api/v1/channels/$channelId/widgets/$widgetId/settings-schema")

    override suspend fun compile(
        channelId: String,
        widgetId: String,
        sourceCode: String,
    ): ApiResult<WidgetVersionDetail> =
        client.postEnvelope(
            "api/v1/channels/$channelId/widgets/$widgetId/compile",
            CompileWidgetBody(sourceCode),
        )

    override suspend fun listVersions(
        channelId: String,
        widgetId: String,
    ): ApiResult<List<WidgetVersionSummary>> =
        when (
            val page: ApiResult<PaginatedEnvelope<WidgetVersionSummary>> =
                client.getDirect("api/v1/channels/$channelId/widgets/$widgetId/versions?page=1&pageSize=50")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun getVersion(
        channelId: String,
        widgetId: String,
        versionId: String,
    ): ApiResult<WidgetVersionDetail> =
        client.getEnvelope("api/v1/channels/$channelId/widgets/$widgetId/versions/$versionId")

    override suspend fun rollback(
        channelId: String,
        widgetId: String,
        versionId: String,
    ): ApiResult<WidgetSummary> =
        client.postEnvelope("api/v1/channels/$channelId/widgets/$widgetId/rollback/$versionId", Unit)

    override suspend fun getProject(channelId: String, widgetId: String): ApiResult<ProjectDto> =
        client.getEnvelope("api/v1/channels/$channelId/widgets/$widgetId/project")

    override suspend fun putProject(
        channelId: String,
        widgetId: String,
        project: ProjectDto,
    ): ApiResult<WidgetVersionDetail> =
        client.putEnvelope("api/v1/channels/$channelId/widgets/$widgetId/project", project)

    override suspend fun listTemplates(channelId: String): ApiResult<List<WidgetTemplate>> =
        client.getEnvelope("api/v1/channels/$channelId/widgets/templates")

    override suspend fun clone(channelId: String, installedWidgetId: String): ApiResult<WidgetSummary> =
        client.postEnvelope(
            "api/v1/channels/$channelId/widgets/clone",
            CloneWidgetBody(installedWidgetId = installedWidgetId),
        )

    // The install route carries no body — the gallery item is addressed entirely by the {galleryItemId} segment;
    // the backend returns 201 with a StatusResponseDto<WidgetDetail>, which postEnvelope unwraps like any 2xx.
    override suspend fun install(channelId: String, galleryItemId: String): ApiResult<WidgetSummary> =
        client.postEnvelope("api/v1/channels/$channelId/widgets/install/$galleryItemId", Unit)

    // Same clone endpoint as [clone], but the fork source is a gallery item — so the body carries galleryItemId
    // instead of installedWidgetId (exactly one is set; the null one is omitted from the wire body).
    override suspend fun cloneFromGallery(channelId: String, galleryItemId: String): ApiResult<WidgetSummary> =
        client.postEnvelope(
            "api/v1/channels/$channelId/widgets/clone",
            CloneWidgetBody(galleryItemId = galleryItemId),
        )
}

/**
 * The update-widget request body (backend `UpdateWidgetRequest`) — every field nullable so an update is a
 * partial patch. A toggle sends only [isEnabled]; a rename sends only [name]; null fields are omitted from
 * the wire body (`explicitNulls = false` on the shared Json). Authored source is NOT patched here — it is
 * compiled via [WidgetsApi.compile].
 */
@Serializable
data class UpdateWidgetBody(
    val name: String? = null,
    val isEnabled: Boolean? = null,
    val settings: JsonObject? = null,
)

/** The create-widget request body (backend `CreateWidgetRequest`). [framework] ∈ `vanilla | vue | react | svelte`. */
@Serializable
data class CreateWidgetBody(val name: String, val framework: String)

/** The compile-on-save request body (backend `CompileWidgetRequest`). */
@Serializable
data class CompileWidgetBody(val sourceCode: String)

/**
 * The clone request body (backend `CloneWidgetRequest`). Exactly one fork source is set: [installedWidgetId] to
 * fork an already-installed widget, or [galleryItemId] to clone straight from the gallery. The unset one stays
 * null and is omitted from the wire body (`explicitNulls = false` on the shared Json).
 */
@Serializable
data class CloneWidgetBody(
    val installedWidgetId: String? = null,
    val galleryItemId: String? = null,
)

/**
 * An overlay widget (backend `WidgetDetail`): its [id], display [name], the source [framework]
 * (`vanilla | vue | react | svelte`), the [source] provenance (`first_party | verified_gallery | custom`),
 * whether it is live, the [overlayUrl] the operator copies into OBS, and [activeVersionId] — the version the
 * overlay currently serves (null until the first successful compile; also the version the editor loads to edit).
 * The authored source and compiled bundle are NOT here — they live on `WidgetVersion` rows. Timestamps and other
 * detail fields are ignored (`ignoreUnknownKeys` on the shared Json).
 */
@Serializable
data class WidgetSummary(
    val id: String = "",
    val name: String = "",
    val description: String? = null,
    val framework: String = "",
    val source: String = "",
    val isEnabled: Boolean = false,
    val overlayUrl: String? = null,
    val activeVersionId: String? = null,
    val eventSubscriptions: List<String> = emptyList(),
    val settings: JsonObject? = null,
)

/**
 * A widget's typed settings schema (backend `WidgetSettingsSchema`): the fields the dashboard renders its generic
 * settings form from, plus the widget's read-only default [eventSubscriptions] (its data wiring, shown for context).
 */
@Serializable
data class WidgetSettingsSchemaDto(
    val widgetKey: String = "",
    val name: String = "",
    val fields: List<WidgetSettingsFieldDto> = emptyList(),
    val eventSubscriptions: List<String> = emptyList(),
)

/**
 * One editable setting (backend `WidgetSettingsField`). [type] picks the control: `bool` (switch), `number`
 * (slider when [min]/[max]/[step] are all set, else a numeric field), `text` (field), `color` (hex field + swatch),
 * `select` (dropdown over [options]), `multiselect` (chips over [options]), `json` (raw-JSON textarea). [default] is
 * the widget's catalogue default for the key (any JSON shape); [group] sections the form.
 */
@Serializable
data class WidgetSettingsFieldDto(
    val key: String = "",
    val label: String = "",
    val type: String = "",
    val group: String = "",
    val default: JsonElement? = null,
    val help: String? = null,
    val options: List<WidgetSettingsFieldOptionDto>? = null,
    val min: Double? = null,
    val max: Double? = null,
    val step: Double? = null,
)

/** A single choice for a `select`/`multiselect` field (backend `WidgetSettingsFieldOption`). */
@Serializable
data class WidgetSettingsFieldOptionDto(val value: String = "", val label: String = "")

/**
 * A starter widget template the create flow offers (backend `WidgetTemplate`): a working, SDK-using [source] to
 * seed into the editor (never a blank editor), tagged with its [framework].
 */
@Serializable
data class WidgetTemplate(
    val key: String = "",
    val name: String = "",
    val description: String = "",
    val framework: String = "",
    val source: String = "",
)

/** A row in a widget's version history (backend `WidgetVersionSummary`) — the rollback / debug list. */
@Serializable
data class WidgetVersionSummary(
    val id: String = "",
    val versionNumber: Int = 0,
    val buildStatus: String = "",
    val contentHash: String? = null,
    val compiledAt: String? = null,
    val createdAt: String = "",
)

/**
 * A single widget version in full (backend `WidgetVersionDetail`). Carries [sourceCode] so the editor can load
 * the current source to edit, and [buildError] / [buildLog] so a failed build is inspectable inline.
 */
@Serializable
data class WidgetVersionDetail(
    val id: String = "",
    val widgetId: String = "",
    val versionNumber: Int = 0,
    val buildStatus: String = "",
    val sourceCode: String? = null,
    val buildError: String? = null,
    val buildLog: String? = null,
    val contentHash: String? = null,
    val compiledAt: String? = null,
    val createdAt: String = "",
)
