// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.bundles.state

import bot.nomnomz.dashboard.core.io.JournalFileIO
import bot.nomnomz.dashboard.core.io.PickedFile
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BundleInspection
import bot.nomnomz.dashboard.core.network.BundlesApi
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.core.network.CommandsApi
import bot.nomnomz.dashboard.core.network.ExportBody
import bot.nomnomz.dashboard.core.network.ExportItemRef
import bot.nomnomz.dashboard.core.network.BundleMetadataBody
import bot.nomnomz.dashboard.core.network.InstalledBundle
import bot.nomnomz.dashboard.core.network.MarketplaceApi
import bot.nomnomz.dashboard.core.network.MarketplaceItem
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.PublishSubmission
import bot.nomnomz.dashboard.core.network.PublisherTokenStatus
import bot.nomnomz.dashboard.core.network.SoundApi
import bot.nomnomz.dashboard.core.network.SoundClip
import bot.nomnomz.dashboard.core.network.WidgetSummary
import bot.nomnomz.dashboard.core.network.WidgetsApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Bundles page state-holder (bundles.md §5–§6): the channel's portable content packs. It resolves the active
// channel, then drives three surfaces off the same holder:
//   • Export — pick from the channel's own content (commands / pipelines / widgets / sounds), build a portable ZIP,
//     and hand the bytes to the OS save dialog.
//   • Import — pick a ZIP, INSPECT it first (manifest / capabilities / issues) so the wizard can block a bad pack,
//     then install it under a conflict policy.
//   • Marketplace — the OPTIONAL hosted catalogue: browsed lazily (it can be unavailable), installed, and — for a
//     publisher — a stored publisher token + a submit-for-review flow.
// The installed list is the source of truth the page always reflects; every install/uninstall re-reads it. A
// marketplace call that returns `503` / `MARKETPLACE_UNAVAILABLE` is rendered as an honest "not available" state,
// never a transient error. Mirrors AutomationController's afterWrite / failWrite / Ready.copy shape.
class BundlesController(
    private val channelsApi: ChannelsApi,
    private val bundlesApi: BundlesApi,
    private val marketplaceApi: MarketplaceApi,
    private val commandsApi: CommandsApi,
    private val pipelinesApi: PipelinesApi,
    private val widgetsApi: WidgetsApi,
    private val soundApi: SoundApi,
    private val fileBridge: JournalFileIO,
) {
    private val _state: MutableStateFlow<BundlesUiState> = MutableStateFlow(BundlesUiState.Loading)

    /** The page render state: loading / ready (installed + exportables + marketplace) / error. */
    val state: StateFlow<BundlesUiState> = _state.asStateFlow()

    private var channelId: String? = null

    // The ZIP the user picked for import, held between [pickAndInspect] and [importInspected] so the confirmed
    // install re-uploads exactly the inspected file. Cleared on install, cancel, or a failed inspect.
    private var pendingImport: PickedFile? = null

    /**
     * Resolve the active channel, then read the installed list (fatal), the channel's exportable content
     * (best-effort — a failing list just leaves that group empty), and the publisher-token status (best-effort).
     * The marketplace is NOT browsed here — it can be unavailable and 503s, so it stays empty until the user opens
     * that tab and browses.
     */
    suspend fun load() {
        if (_state.value !is BundlesUiState.Ready) _state.value = BundlesUiState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = BundlesUiState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        val installed: List<InstalledBundle> =
            when (val result: ApiResult<List<InstalledBundle>> = bundlesApi.installed(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = BundlesUiState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        val exportables: Exportables = loadExportables(channel.id)
        val hasToken: Boolean =
            when (val result: ApiResult<PublisherTokenStatus> = marketplaceApi.publisherToken(channel.id)) {
                is ApiResult.Ok -> result.value.hasToken
                is ApiResult.Failure -> false
            }

        _state.value =
            BundlesUiState.Ready(
                installed = installed,
                exportables = exportables,
                hasPublisherToken = hasToken,
            )
    }

    /**
     * Build a portable ZIP from the picked [items] + metadata and hand it to the OS save dialog. On success a
     * saved file sets a notice; a cancelled save just returns to idle. A backend failure surfaces the error.
     */
    suspend fun exportBundle(
        items: List<ExportItemRef>,
        name: String,
        version: String,
        description: String?,
    ) {
        val id: String = channelId ?: return failWrite(NoChannelError)
        val body: ExportBody =
            ExportBody(
                items = items,
                metadata = BundleMetadataBody(name = name, version = version, description = description),
            )
        when (val result: ApiResult<ByteArray> = bundlesApi.export(id, body)) {
            is ApiResult.Ok -> {
                val saved: Boolean = fileBridge.saveFile(suggestedName = "$name.zip", bytes = result.value)
                if (saved) setNotice(ExportedNotice) else clearTransient()
            }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /**
     * Pick a bundle ZIP and inspect it (WITHOUT installing). Holds the picked file for the subsequent import and
     * stages the [BundleInspection] on the Ready state so the wizard can show the manifest / capabilities / issues.
     * A cancelled pick is a no-op; a failed inspect clears the staged file and surfaces the error.
     */
    suspend fun pickAndInspect() {
        val id: String = channelId ?: return failWrite(NoChannelError)
        val picked: PickedFile = fileBridge.pickFile() ?: return
        pendingImport = picked
        when (val result: ApiResult<BundleInspection> = bundlesApi.inspect(id, picked.name, picked.bytes)) {
            is ApiResult.Ok -> setReady { it.copy(inspection = result.value, actionError = null, notice = null) }
            is ApiResult.Failure -> {
                pendingImport = null
                failWrite(result.error.message)
            }
        }
    }

    /**
     * Install the previously-inspected bundle under a conflict [policy]. Refuses when there is no staged file or
     * the inspection reported blocking issues. On success clears the staged inspection, re-reads the installed
     * list, and sets a notice.
     */
    suspend fun importInspected(policy: String) {
        val id: String = channelId ?: return failWrite(NoChannelError)
        val picked: PickedFile = pendingImport ?: return failWrite(NoStagedImport)
        val inspection: BundleInspection? = (_state.value as? BundlesUiState.Ready)?.inspection
        if (inspection == null || inspection.issues.isNotEmpty()) return failWrite(ImportHasIssues)

        when (val result: ApiResult<InstalledBundle> = bundlesApi.import(id, picked.name, picked.bytes, policy)) {
            is ApiResult.Ok -> {
                pendingImport = null
                reloadInstalled()
                setReady { it.copy(inspection = null, notice = ImportedNotice, actionError = null) }
            }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /** Drop the staged inspection + picked file (the user cancelled the import wizard). */
    fun clearInspection() {
        pendingImport = null
        setReady { it.copy(inspection = null) }
    }

    /** Uninstall an installed bundle by its [id] — removes exactly what that pack created. Re-lists on success. */
    suspend fun uninstall(id: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        when (val result: ApiResult<Unit> = bundlesApi.uninstall(channel, id)) {
            is ApiResult.Ok -> reloadInstalled()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /**
     * Browse the hosted marketplace (lazy — only when the user opens that tab). On success fills the list and
     * marks it available. A `503` / `MARKETPLACE_UNAVAILABLE` is NOT an error: it flips [Ready.marketplaceAvailable]
     * off so the screen shows the honest "not available" state; any other failure surfaces as an action error.
     */
    suspend fun browseMarketplace(q: String?, type: String?) {
        val id: String = channelId ?: return failWrite(NoChannelError)
        when (val result: ApiResult<List<MarketplaceItem>> = marketplaceApi.items(id, q, type, null, 1, 50)) {
            is ApiResult.Ok ->
                setReady {
                    it.copy(marketplace = result.value, marketplaceAvailable = true, actionError = null, notice = null)
                }
            is ApiResult.Failure ->
                if (isUnavailable(result.error)) {
                    setReady {
                        it.copy(marketplace = emptyList(), marketplaceAvailable = false, actionError = null, notice = null)
                    }
                } else {
                    failWrite(result.error.message)
                }
        }
    }

    /**
     * Install a catalogue item into the channel under a conflict [policy]. On success re-reads the installed list
     * and sets a notice. An unavailable marketplace flips the availability flag; any other failure is an error.
     */
    suspend fun installFromMarketplace(itemId: String, policy: String) {
        val id: String = channelId ?: return failWrite(NoChannelError)
        when (val result: ApiResult<InstalledBundle> = marketplaceApi.install(id, itemId, policy)) {
            is ApiResult.Ok -> {
                reloadInstalled()
                setReady { it.copy(notice = InstalledNotice, actionError = null) }
            }
            is ApiResult.Failure ->
                if (isUnavailable(result.error)) setReady { it.copy(marketplaceAvailable = false, actionError = null) }
                else failWrite(result.error.message)
        }
    }

    /**
     * Submit a bundle ZIP for review: pick the file, then publish it with the given metadata. On success sets a
     * notice carrying the returned submission id. An unavailable marketplace flips the flag; else an error.
     */
    suspend fun publish(name: String, version: String, summary: String, tagsCsv: String) {
        val id: String = channelId ?: return failWrite(NoChannelError)
        val picked: PickedFile = fileBridge.pickFile() ?: return
        when (
            val result: ApiResult<PublishSubmission> =
                marketplaceApi.publish(id, picked.name, picked.bytes, name, version, summary, tagsCsv)
        ) {
            is ApiResult.Ok -> setReady { it.copy(notice = "$SubmittedNotice ${result.value.submissionId}", actionError = null) }
            is ApiResult.Failure ->
                if (isUnavailable(result.error)) setReady { it.copy(marketplaceAvailable = false, actionError = null) }
                else failWrite(result.error.message)
        }
    }

    /** Store (or replace) the publisher token (write-only), then refresh the stored-token status. */
    suspend fun setPublisherToken(token: String) {
        val id: String = channelId ?: return failWrite(NoChannelError)
        when (val result: ApiResult<Unit> = marketplaceApi.setPublisherToken(id, token)) {
            is ApiResult.Ok -> refreshPublisherToken(id)
            is ApiResult.Failure ->
                if (isUnavailable(result.error)) setReady { it.copy(marketplaceAvailable = false, actionError = null) }
                else failWrite(result.error.message)
        }
    }

    /** Remove the stored publisher token, then refresh the status. */
    suspend fun clearPublisherToken() {
        val id: String = channelId ?: return failWrite(NoChannelError)
        when (val result: ApiResult<Unit> = marketplaceApi.clearPublisherToken(id)) {
            is ApiResult.Ok -> refreshPublisherToken(id)
            is ApiResult.Failure ->
                if (isUnavailable(result.error)) setReady { it.copy(marketplaceAvailable = false, actionError = null) }
                else failWrite(result.error.message)
        }
    }

    // ── internals ────────────────────────────────────────────────────────────

    // Read the channel's own content for the export pick-list. Each list is best-effort: a failing one just leaves
    // that group empty rather than failing the whole page. Sounds are channel-scoped server-side, so its list takes
    // no channel id; the display name falls back to the slug when the human label is blank.
    private suspend fun loadExportables(id: String): Exportables {
        val commands: List<Pair<String, String>> =
            when (val result: ApiResult<List<CommandSummary>> = commandsApi.list(id)) {
                is ApiResult.Ok -> result.value.map { it.id to it.name }
                is ApiResult.Failure -> emptyList()
            }
        val pipelines: List<Pair<String, String>> =
            when (val result: ApiResult<List<PipelineSummary>> = pipelinesApi.list(id)) {
                is ApiResult.Ok -> result.value.map { it.id to it.name }
                is ApiResult.Failure -> emptyList()
            }
        val widgets: List<Pair<String, String>> =
            when (val result: ApiResult<List<WidgetSummary>> = widgetsApi.list(id)) {
                is ApiResult.Ok -> result.value.map { it.id to it.name }
                is ApiResult.Failure -> emptyList()
            }
        val sounds: List<Pair<String, String>> =
            when (val result: ApiResult<List<SoundClip>> = soundApi.list()) {
                is ApiResult.Ok -> result.value.map { it.id to it.displayName.ifBlank { it.name } }
                is ApiResult.Failure -> emptyList()
            }
        return Exportables(commands = commands, pipelines = pipelines, widgets = widgets, sounds = sounds)
    }

    // Re-read the installed list and swap it onto the current Ready state, preserving the rest. A failure surfaces
    // as an action error over the kept content.
    private suspend fun reloadInstalled() {
        val id: String = channelId ?: return
        when (val result: ApiResult<List<InstalledBundle>> = bundlesApi.installed(id)) {
            is ApiResult.Ok -> setReady { it.copy(installed = result.value) }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private suspend fun refreshPublisherToken(id: String) {
        when (val result: ApiResult<PublisherTokenStatus> = marketplaceApi.publisherToken(id)) {
            is ApiResult.Ok -> setReady { it.copy(hasPublisherToken = result.value.hasToken, actionError = null) }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun isUnavailable(error: ApiError): Boolean =
        error.code == MarketplaceUnavailableCode || error.status == 503

    private fun setReady(transform: (BundlesUiState.Ready) -> BundlesUiState.Ready) {
        val current: BundlesUiState.Ready = _state.value as? BundlesUiState.Ready ?: return
        _state.value = transform(current)
    }

    private fun setNotice(message: String) {
        setReady { it.copy(notice = message, actionError = null) }
    }

    private fun clearTransient() {
        setReady { it.copy(notice = null, actionError = null) }
    }

    private fun failWrite(detail: String) {
        val current: BundlesUiState = _state.value
        _state.value =
            if (current is BundlesUiState.Ready) current.copy(actionError = detail, notice = null)
            else BundlesUiState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
        const val NoStagedImport: String = "Choose a bundle file to inspect first."
        const val ImportHasIssues: String = "This bundle has blocking issues and can't be imported."
        const val MarketplaceUnavailableCode: String = "MARKETPLACE_UNAVAILABLE"
        const val ExportedNotice: String = "Bundle exported."
        const val ImportedNotice: String = "Bundle installed."
        const val InstalledNotice: String = "Installed from the marketplace."
        const val SubmittedNotice: String = "Submitted for review —"
    }
}

/**
 * The channel's own content available to export, grouped by type. Each entry is a (id, displayName) pair — the id
 * builds the [ExportItemRef], the name labels the checkbox row.
 */
data class Exportables(
    val commands: List<Pair<String, String>>,
    val pipelines: List<Pair<String, String>>,
    val widgets: List<Pair<String, String>>,
    val sounds: List<Pair<String, String>>,
)

/** The Bundles page render state. */
sealed interface BundlesUiState {
    data object Loading : BundlesUiState

    /**
     * The page content. [installed] is the source-of-truth list the page always reflects; [exportables] is the
     * channel's own content offered to export; [inspection] is the staged import preview (non-null only while the
     * import wizard is showing a picked ZIP). [marketplace] fills only after the user browses; [marketplaceAvailable]
     * is false when the hosted catalogue returned unavailable. [hasPublisherToken] drives the publish card's stored
     * state. [actionError] / [notice] are transient banners — at most one set at a time.
     */
    data class Ready(
        val installed: List<InstalledBundle>,
        val exportables: Exportables,
        val inspection: BundleInspection? = null,
        val marketplace: List<MarketplaceItem> = emptyList(),
        val marketplaceAvailable: Boolean = true,
        val hasPublisherToken: Boolean = false,
        val actionError: String? = null,
        val notice: String? = null,
    ) : BundlesUiState

    data class Error(val detail: String) : BundlesUiState
}
