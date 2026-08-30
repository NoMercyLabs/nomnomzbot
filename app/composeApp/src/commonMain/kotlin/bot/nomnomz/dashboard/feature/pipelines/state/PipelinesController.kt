// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.state

import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.core.realtime.onConfigChange
import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.CodeScriptSummary
import bot.nomnomz.dashboard.core.network.CodeScriptsApi
import bot.nomnomz.dashboard.core.network.CreateScriptBody
import bot.nomnomz.dashboard.core.network.EconomyApi
import bot.nomnomz.dashboard.core.network.Giveaway
import bot.nomnomz.dashboard.core.network.GiveawaysApi
import bot.nomnomz.dashboard.core.network.Quote
import bot.nomnomz.dashboard.core.network.QuotesApi
import bot.nomnomz.dashboard.core.network.SavingsJar
import bot.nomnomz.dashboard.core.network.SoundApi
import bot.nomnomz.dashboard.core.network.SoundClip
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.TtsVoice
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreatePipelineBody
import bot.nomnomz.dashboard.core.network.OutboundWebhook
import bot.nomnomz.dashboard.core.network.PickList
import bot.nomnomz.dashboard.core.network.PickListsApi
import bot.nomnomz.dashboard.core.network.PipelineCatalogue
import bot.nomnomz.dashboard.core.network.PipelineCatalogueRemote
import bot.nomnomz.dashboard.core.network.PipelineDetail
import bot.nomnomz.dashboard.core.network.PipelineBlastRadiusSummary
import bot.nomnomz.dashboard.core.network.PipelineOptionsApi
import bot.nomnomz.dashboard.core.network.PipelineGraph
import bot.nomnomz.dashboard.core.network.PipelineNode
import bot.nomnomz.dashboard.core.network.PipelineStep
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelineTestRunBody
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.RuntimePalette
import bot.nomnomz.dashboard.core.network.TestRunResult
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import bot.nomnomz.dashboard.core.network.WebhooksApi
import bot.nomnomz.dashboard.core.network.WidgetSummary
import bot.nomnomz.dashboard.core.network.WidgetsApi
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonPrimitive
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_pipeline_deleted
import nomnomzbot.composeapp.generated.resources.feedback_pipeline_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_pipeline_saved

// The Pipelines page state-holder (the visual automation engine). Two surfaces in one flow:
//   1. the LIST of the channel's real pipelines (no fabricated rows), with create / rename / toggle / delete;
//   2. the action-chain EDITOR for a selected pipeline — add / remove / reorder / configure the ordered action
//      blocks (with an optional condition + stop-on-match per step), then save the whole chain.
// Every write reloads the affected surface so the screen always reflects the backend's truth: a list write
// re-lists; a chain save re-fetches the pipeline's detail. The screen renders [state]; a retry calls [load].
class PipelinesController(
    private val channelsApi: ChannelsApi,
    private val pipelinesApi: PipelinesApi,
    private val webhooksApi: WebhooksApi,
    private val pickListsApi: PickListsApi,
    // The widget list source for the `widget_event` block's widget picker. Optional (best-effort like every editor
    // picker source): when absent the widget field degrades to free-text id entry, exactly as an empty list does.
    private val widgetsApi: WidgetsApi? = null,
    // Optional (best-effort like every editor picker source): when absent the corresponding raw entity-id field
    // simply gets an empty picker list. Each feeds one `TypedParamFields` reference picker in the step editor.
    private val soundApi: SoundApi? = null,
    private val ttsApi: TtsApi? = null,
    private val economyApi: EconomyApi? = null,
    private val codeScriptsApi: CodeScriptsApi? = null,
    private val giveawaysApi: GiveawaysApi? = null,
    private val quotesApi: QuotesApi? = null,
    // The rich resource-picker option source (S-RICH-PICKERS) — best-effort like every editor picker source
    // above: absent, the picker-kind fields degrade to their legacy local-list/free-text entry.
    private val pipelineOptionsApi: PipelineOptionsApi? = null,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<PipelinesState> = MutableStateFlow(PipelinesState.Loading)

    /** The page render state: loading / list (ready/empty) / editing a pipeline's chain / error. */
    val state: StateFlow<PipelinesState> = _state.asStateFlow()

    // The channel every read/write targets — resolved by [load] and reused so a mutation never re-resolves it.
    private var channelId: String? = null

    // The builder palette, fetched from the backend registry once per load and reused by every editor open so
    // the block list can never drift. Falls back to the locally-known blocks if the fetch fails.
    private var palette: RuntimePalette = PipelineCatalogue.fallbackPalette()

    /** Resolve the active channel, fetch the block palette, then list its pipelines. Returns to the list view. */
    /**
     * Keeps this page live: the backend announces every change to pipelines on the dashboard hub, from any
     * operator or from the bot itself, and this refetches instead of leaving whatever was on screen when
     * the page opened. Without it the only way to see a change was a manual reload.
     */
    suspend fun subscribeToHub(hubEvents: SharedFlow<HubEvent>) {
        hubEvents.onConfigChange("pipelines") { load() }
    }

    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is PipelinesState.Ready) _state.value = PipelinesState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = PipelinesState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        // Refresh the palette from the backend registry. A failure is non-fatal — keep the fallback so the
        // editor still opens with the core blocks; the pipeline LIST is what the page needs to render.
        when (val result: ApiResult<PipelineCatalogueRemote> = pipelinesApi.catalogue(channel.id)) {
            is ApiResult.Ok -> palette = PipelineCatalogue.buildPalette(result.value)
            is ApiResult.Failure -> palette = PipelineCatalogue.fallbackPalette()
        }

        loadList(channel.id)
    }

    private suspend fun loadList(channel: String) {
        when (val result: ApiResult<List<PipelineSummary>> = pipelinesApi.list(channel)) {
            is ApiResult.Failure -> _state.value = PipelinesState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) PipelinesState.Empty
                    else PipelinesState.Ready(result.value)
        }
    }

    // ── List-level writes ────────────────────────────────────────────────────

    /** Create a pipeline (empty starter chain), then reload the list so the new row appears. */
    suspend fun createPipeline(name: String, description: String?) {
        val channel: String = channelId ?: return failList(NoChannelError)
        val body =
            CreatePipelineBody(
                name = name,
                description = description?.takeIf { it.isNotBlank() },
                graph = PipelineGraph().toJson(),
            )
        afterListWrite(pipelinesApi.create(channel, body))
    }

    /** Rename / re-describe a pipeline, then reload the list. */
    suspend fun renamePipeline(id: String, name: String, description: String?) {
        val channel: String = channelId ?: return failList(NoChannelError)
        afterListWrite(
            pipelinesApi.update(
                channel,
                id,
                UpdatePipelineBody(name = name, description = description ?: ""),
            )
        )
    }

    /** Flip a pipeline's enabled flag via the update endpoint (no dedicated toggle route). Reloads the list. */
    suspend fun togglePipeline(id: String, enabled: Boolean) {
        val channel: String = channelId ?: return failList(NoChannelError)
        afterListWrite(pipelinesApi.update(channel, id, UpdatePipelineBody(isEnabled = enabled)))
    }

    /** Delete a pipeline, then reload the list. */
    suspend fun deletePipeline(id: String) {
        val channel: String = channelId ?: return failList(NoChannelError)
        afterListWrite(pipelinesApi.delete(channel, id), success = Res.string.feedback_pipeline_deleted)
    }

    /**
     * Create a brand-new code script named [name] with an empty starter body, for the `run_code` step's
     * create-and-bind flow (S046-code-tier-link): the operator never has to leave the pipeline editor to first
     * make a script on the Code Scripts page before binding it. Returns the new script as a [PickerOption]
     * (id + label) so the caller can both select it in the field AND immediately open its real editor — a null
     * [codeScriptsApi] (feature not wired for this deployment) or a failed create surfaces as `null`, leaving the
     * field's create-mode open so the operator can retry.
     */
    suspend fun createCodeScript(name: String): PickerOption? {
        val api: CodeScriptsApi = codeScriptsApi ?: return null
        return when (val result: ApiResult<CodeScriptSummary> = api.create(CreateScriptBody(name = name, sourceCode = ""))) {
            is ApiResult.Ok -> labeledOption(result.value.id, result.value.name, "Code script")
            is ApiResult.Failure -> null
        }
    }

    /**
     * The real, counted blast radius for deleting [id] — the delete confirm dialog calls this to render the
     * dependents BEFORE the destructive delete can proceed (S-CONSEQ-b). No channel resolved yet is a genuine
     * failure, not a silent zero — the dialog must show its own "could not check" message, never an empty
     * radius that reads as safe.
     */
    suspend fun fetchBlastRadius(id: String): ApiResult<PipelineBlastRadiusSummary> {
        val channel: String =
            channelId ?: return ApiResult.Failure(ApiError(status = 0, code = "NO_CHANNEL", message = NoChannelError))
        return pipelinesApi.blastRadius(channel, id)
    }

    // ── Open / close the chain editor ────────────────────────────────────────

    /** Open the action-chain editor for [pipeline]: fetch its detail, decode its chain, load picker options. */
    suspend fun openEditor(pipeline: PipelineSummary) {
        val channel: String = channelId ?: return failList(NoChannelError)
        _state.value = PipelinesState.Loading
        val options: EditorOptions = loadEditorOptions(channel)
        when (val result: ApiResult<PipelineDetail> = pipelinesApi.get(channel, pipeline.id)) {
            is ApiResult.Failure -> _state.value = PipelinesState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    PipelinesState.Editing(
                        pipelineId = result.value.id,
                        name = result.value.name,
                        steps = backfillIds(result.value.chain.steps),
                        palette = palette,
                        options = options,
                    )
        }
    }

    // The editor's cross-feature dropdown options: the channel's outbound webhook endpoints (for the
    // `send_webhook` block's endpoint picker) and pick-list names (for `pick_from_list`). Both are best-effort —
    // a failure yields an empty list and the field degrades to free text, never blocking the editor.
    private suspend fun loadEditorOptions(channel: String): EditorOptions {
        val endpoints: List<PickerOption> =
            when (val result: ApiResult<List<OutboundWebhook>> = webhooksApi.listOutbound(channel)) {
                is ApiResult.Ok -> result.value.map { labeledOption(it.id, it.name, "Webhook") }
                is ApiResult.Failure -> emptyList()
            }
        val pickLists: List<PickerOption> =
            when (val result: ApiResult<List<PickList>> = pickListsApi.list()) {
                is ApiResult.Ok -> result.value.map { labeledOption(it.name, it.name, "Pick list") }
                is ApiResult.Failure -> emptyList()
            }
        // The channel's overlay widgets (for `widget_event`). Absent widgetsApi or a failed fetch → empty → the
        // widget field falls back to free-text id entry.
        val widgets: List<PickerOption> =
            when (val result: ApiResult<List<WidgetSummary>>? = widgetsApi?.list(channel)) {
                is ApiResult.Ok -> result.value.map { labeledOption(it.id, it.name, "Widget") }
                else -> emptyList()
            }
        // The channel's pipelines by NAME (for `schedule_pipeline` / `run_pipeline`) — the action resolves the
        // pipeline by its name. The same fetch also yields each pipeline's declared parameter names
        // (S-PIPE-TREE-d2b-UI), keyed by name, so the `run_pipeline` block's argument editor can tell — once a
        // target is picked — whether to render one labelled field per declared name or fall back to the
        // generic positional `args` editor.
        val pipelineList: List<PipelineSummary> =
            when (val result: ApiResult<List<PipelineSummary>> = pipelinesApi.list(channel)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> emptyList()
            }
        val pipelines: List<PickerOption> =
            pipelineList.map { labeledOption(it.name, it.name, "Pipeline") }
        val pipelineParameterNames: Map<String, List<String>> =
            pipelineList
                .filter { !it.parameterNames.isNullOrEmpty() }
                .associate { it.name to it.parameterNames.orEmpty() }
        // The remaining cross-feature entity references the step editor can pick (play_sound, play_tts,
        // jar_contribute, run_code, giveaway_*, post_quote). Each is best-effort: a null API or a failed fetch
        // yields an empty list and that field simply shows an empty picker.
        val soundClips: List<PickerOption> =
            when (val result: ApiResult<List<SoundClip>>? = soundApi?.list()) {
                is ApiResult.Ok -> result.value.map { labeledOption(it.id, it.name, "Sound clip") }
                else -> emptyList()
            }
        val ttsVoices: List<PickerOption> =
            when (val result: ApiResult<List<TtsVoice>>? = ttsApi?.voices(channel)) {
                is ApiResult.Ok -> result.value.map { labeledOption(it.id, it.name, "Voice") }
                else -> emptyList()
            }
        val jars: List<PickerOption> =
            when (val result: ApiResult<List<SavingsJar>>? = economyApi?.savingsJars(channel)) {
                is ApiResult.Ok -> result.value.map { labeledOption(it.id, it.name, "Savings jar") }
                else -> emptyList()
            }
        val codeScripts: List<PickerOption> =
            when (val result: ApiResult<List<CodeScriptSummary>>? = codeScriptsApi?.list()) {
                is ApiResult.Ok -> result.value.map { labeledOption(it.id, it.name, "Code script") }
                else -> emptyList()
            }
        val giveaways: List<PickerOption> =
            when (val result: ApiResult<List<Giveaway>>? = giveawaysApi?.list()) {
                is ApiResult.Ok -> result.value.map { labeledOption(it.id, it.title, "Giveaway") }
                else -> emptyList()
            }
        val quotes: List<PickerOption> =
            when (val result: ApiResult<List<Quote>>? = quotesApi?.list()) {
                is ApiResult.Ok ->
                    result.value.map { PickerOption(value = it.number.toString(), label = "#${it.number} ${it.text}") }
                else -> emptyList()
            }
        return EditorOptions(
            outboundEndpoints = endpoints,
            pickLists = pickLists,
            widgets = widgets,
            pipelines = pipelines,
            pipelineParameterNames = pipelineParameterNames,
            soundClips = soundClips,
            ttsVoices = ttsVoices,
            jars = jars,
            codeScripts = codeScripts,
            giveaways = giveaways,
            quotes = quotes,
            pipelineOptionsApi = pipelineOptionsApi,
        )
    }

    /** Leave the editor and return to the list (discarding any unsaved chain changes). */
    suspend fun closeEditor() {
        val channel: String = channelId ?: return
        loadList(channel)
    }

    // ── Chain edits (operate on the in-memory Editing state; persisted by [saveChain]) ──

    /** Append a new step (its action + optional condition) to the end of the edited chain. */
    fun addStep(step: PipelineStep) = mutateChain { it + step }

    /** Replace the step at [index] with [step] (a re-configure of its action/condition/stop flag). */
    fun updateStep(index: Int, step: PipelineStep) =
        mutateChain { current ->
            if (index !in current.indices) current
            else current.toMutableList().also { it[index] = step }
        }

    /** Remove the step at [index] from the edited chain. */
    fun removeStep(index: Int) =
        mutateChain { current ->
            if (index !in current.indices) current
            else current.toMutableList().also { it.removeAt(index) }
        }

    /** Move the step at [index] one position earlier (no-op at the top). */
    fun moveStepUp(index: Int) =
        mutateChain { current ->
            if (index <= 0 || index >= current.size) current
            else current.toMutableList().also { it.add(index - 1, it.removeAt(index)) }
        }

    /** Move the step at [index] one position later (no-op at the bottom). */
    fun moveStepDown(index: Int) =
        mutateChain { current ->
            if (index < 0 || index >= current.size - 1) current
            else current.toMutableList().also { it.add(index + 1, it.removeAt(index)) }
        }

    // ── Branching ("if" block) edits (S046-branching-if) ───────────────────────
    //
    // The wire model's tree-nesting fields (id/parentStepId/branch/blockKind/blockConfig/order) shipped inert
    // in S046-branching-prereq. This is the first thing that actually writes them: an "if" block is a step with
    // no runnable action of its own (blockKind = "if", blockConfig = its condition), and its "then"/"else"
    // lanes are ordinary steps that point back at it via parentStepId/branch. The flat add/remove/reorder
    // methods above stay untouched — they still operate on the un-nested root chain exactly as before; these
    // new methods are lane-aware and only ever touch the (parentStepId, branch) group they're asked about.

    private var nextLocalStepId: Int = 1

    // A client-only id for a step that doesn't have a backend-assigned one yet (a brand-new block or lane
    // child). Not a UUID — only needs to be unique within this edit session so parentStepId can address it
    // before the next save round-trips the backend's real id back.
    private fun newLocalStepId(): String = "local-${nextLocalStepId++}"

    /**
     * Add a new "if" block at the end of the root chain: a block-kind step gated by [condition], with no
     * action of its own to run. The wire model's `action` field is non-nullable, so a block step encodes a
     * sentinel `PipelineNode(type = "block")` — the backend does not yet execute BlockKind steps (that engine
     * work is out of scope for this slice), so this sentinel is this slice's own assumption, not a shape read
     * from the engine; see the slice report. Returns the new step's id so the caller can immediately target
     * its "then"/"else" lanes with [addBranchStep].
     */
    fun addIfBlock(condition: PipelineNode): String {
        val editing: PipelinesState.Editing = _state.value as? PipelinesState.Editing ?: return ""
        val id: String = newLocalStepId()
        val order: Int = editing.steps.count { it.parentStepId == null }
        val step =
            PipelineStep(
                action = PipelineNode(type = "block"),
                blockKind = "if",
                condition = condition,
                id = id,
                order = order,
            )
        mutateChain { it + step }
        return id
    }

    /**
     * Add a new "switch" block at the end of the root chain: a block-kind step with no action of its own,
     * whose switch [value] is carried in `blockConfig` — never `condition` — because the engine's
     * `ExecuteSwitchAsync` reads a switch step's `BlockConfigJson` as `SwitchBlockConfig { value }` and never
     * looks at its `Conditions` (unlike an "if" block, which is the other way around). Returns the new step's
     * id so the caller can attach `switch_case` children to it with [addBranchStep] (branch = null — a
     * switch's cases are its only lane, so no branch label is needed to keep them apart from anything else).
     */
    fun addSwitchBlock(value: String): String {
        val editing: PipelinesState.Editing = _state.value as? PipelinesState.Editing ?: return ""
        val id: String = newLocalStepId()
        val order: Int = editing.steps.count { it.parentStepId == null }
        val step =
            PipelineStep(
                action = PipelineNode(type = "block"),
                blockKind = "switch",
                blockConfig = JsonObject(mapOf("value" to JsonPrimitive(value))),
                id = id,
                order = order,
            )
        mutateChain { it + step }
        return id
    }

    /**
     * Add a new "random_branch" block at the end of the root chain: a block-kind step with no action or config
     * of its own — the engine's `ExecuteRandomBranchAsync` reads nothing off the block step itself, only its
     * `random_case` children (each weighted via `RandomCaseBlockConfig { weight }`, PipelineTreeTypes.cs:160,
     * `PipelineEngine.cs:1907`), picked by a weighted roll across them (`PipelineEngine.cs:1902-1928`). Returns
     * the new step's id so the caller can attach `random_case` children with [addBranchStep] (branch = null —
     * exactly like a "switch" block's cases, `parentStepId` alone already disambiguates the lane).
     */
    fun addRandomBranchBlock(): String {
        val editing: PipelinesState.Editing = _state.value as? PipelinesState.Editing ?: return ""
        val id: String = newLocalStepId()
        val order: Int = editing.steps.count { it.parentStepId == null }
        val step =
            PipelineStep(
                action = PipelineNode(type = "block"),
                blockKind = "random_branch",
                id = id,
                order = order,
            )
        mutateChain { it + step }
        return id
    }

    /**
     * Add a new "loop" block at the end of the root chain: a block-kind step with no action of its own, whose
     * iteration config is carried by [mode]/[count]/[listVar]/[maxIterations]/[maxLoopRuntimeSeconds] — read by
     * the engine's `ExecuteLoopAsync` as `LoopBlockConfig { mode, count, list_var, max_iterations,
     * max_loop_runtime_seconds }` off `BlockConfigJson` (PipelineTreeTypes.cs:137). For `mode == "while"` the
     * engine evaluates the loop step's own `Conditions` on every pass (`PipelineEngine.cs:1779`) — the SAME
     * `condition` field an ordinary leaf/if-block step uses, never `blockConfig` — so [whileCondition] is
     * threaded there, not into the config JSON. `ExecuteLoopAsync` walks `node.Children` with no branch filter
     * (PipelineEngine.cs:1821), so the loop's body lane needs no branch label — [addBranchStep] with
     * `branch = null` addresses it via `parentStepId` alone, exactly like a "switch" block's cases. Returns the
     * new step's id so the caller can attach body steps to it.
     */
    fun addLoopBlock(
        mode: String,
        count: Int? = null,
        listVar: String? = null,
        maxIterations: Int? = null,
        maxLoopRuntimeSeconds: Int? = null,
        whileCondition: PipelineNode? = null,
    ): String {
        val editing: PipelinesState.Editing = _state.value as? PipelinesState.Editing ?: return ""
        val id: String = newLocalStepId()
        val order: Int = editing.steps.count { it.parentStepId == null }
        val step =
            PipelineStep(
                action = PipelineNode(type = "block"),
                blockKind = "loop",
                condition = whileCondition,
                blockConfig = encodeLoopConfig(mode, count, listVar, maxIterations, maxLoopRuntimeSeconds),
                id = id,
                order = order,
            )
        mutateChain { it + step }
        return id
    }

    /**
     * Add a new "try" block at the end of the root chain: a block-kind step with no action, condition, or
     * config of its own. The engine's `ExecuteTryAsync` reads only its children — the body lane is every child
     * with `Branch == "then"` and the catch lane every child with `Branch == "else"` (PipelineEngine.cs:1957-
     * 1961) — the exact same two branch labels an "if" block uses for its own then/else lanes, just repurposed
     * here as try/catch; the engine never distinguishes them by any other field. A failure anywhere in the body
     * lane (`FailedBreak`) routes execution into the catch lane once the body finishes (PipelineEngine.cs:2023-
     * 2036); nothing is exposed to the catch lane via a template variable — the engine folds the catch state
     * back into the parent's counts without adding any error/exception detail to the execution context.
     * Returns the new step's id so the caller can attach lane children with [addBranchStep].
     */
    fun addTryBlock(): String {
        val editing: PipelinesState.Editing = _state.value as? PipelinesState.Editing ?: return ""
        val id: String = newLocalStepId()
        val order: Int = editing.steps.count { it.parentStepId == null }
        val step =
            PipelineStep(
                action = PipelineNode(type = "block"),
                blockKind = "try",
                id = id,
                order = order,
            )
        mutateChain { it + step }
        return id
    }

    /**
     * Append [step] to the [branch] ("then"/"else") lane of the block [parentStepId]. Assigns [step] a local
     * id if it doesn't already carry one, and an `order` scoped to just that lane — every other lane (the
     * block's other branch, a sibling block's lanes, the root chain) keeps its own order values untouched.
     *
     * [branch] is null for a block kind that only ever has one lane of children under a given parent (a
     * "switch" block's `switch_case` children, or a `switch_case`'s own body steps) — [parentStepId] alone
     * already disambiguates that lane from every sibling, so no branch label is needed to keep it apart.
     */
    fun addBranchStep(parentStepId: String, branch: String?, step: PipelineStep) =
        mutateChain { current ->
            val order: Int = current.count { it.parentStepId == parentStepId && it.branch == branch }
            current +
                step.copy(
                    id = step.id ?: newLocalStepId(),
                    parentStepId = parentStepId,
                    branch = branch,
                    order = order,
                )
        }

    /**
     * Remove the lane step [stepId] — and, if it is itself a nested block, everything under it — then compact
     * the `order` values of just the lane it lived in back to a dense 0..n-1 run. Every step outside that one
     * lane (its parent, the other branch, sibling blocks) is left exactly as it was.
     */
    fun removeBranchStep(stepId: String) =
        mutateChain { current ->
            val target: PipelineStep = current.firstOrNull { it.id == stepId } ?: return@mutateChain current
            val descendants: Set<String> = descendantIds(current, stepId)
            val remaining: List<PipelineStep> = current.filterNot { it.id == stepId || it.id in descendants }
            reindexLane(remaining, target.parentStepId, target.branch)
        }

    /** Move [stepId] one position earlier within its own lane (no-op already at the top of that lane). */
    fun moveBranchStepUp(stepId: String) = swapWithLaneSibling(stepId, offset = -1)

    /** Move [stepId] one position later within its own lane (no-op already at the bottom of that lane). */
    fun moveBranchStepDown(stepId: String) = swapWithLaneSibling(stepId, offset = 1)

    /**
     * Append [step] to the root chain (no parent block) the same way [addBranchStep] appends to a lane — an
     * id-carrying, order-scoped root entry. Once a chain contains at least one "if" block, the tree UI adds
     * every step (root or lane) through the id-based methods on this page, so root order stays governed by
     * the same rules as every other lane; the legacy index-based [addStep] above is kept only for the
     * never-nested chains it always served.
     */
    fun addRootStep(step: PipelineStep) =
        mutateChain { current ->
            val order: Int = current.count { it.parentStepId == null }
            current + step.copy(id = step.id ?: newLocalStepId(), parentStepId = null, branch = null, order = order)
        }

    /**
     * Replace the step [stepId] (root or lane, leaf or block) with [step]'s action/condition/stop-flag/
     * block-kind/block-config — its id/parentStepId/branch/order are always kept from the step already there,
     * so editing a step's configuration never re-parents, re-branches, or reorders it.
     */
    fun updateStepById(stepId: String, step: PipelineStep) =
        mutateChain { current ->
            val index: Int = current.indexOfFirst { it.id == stepId }
            if (index < 0) current
            else
                current.toMutableList().also {
                    val existing: PipelineStep = it[index]
                    it[index] =
                        step.copy(
                            id = existing.id,
                            parentStepId = existing.parentStepId,
                            branch = existing.branch,
                            order = existing.order,
                        )
                }
        }

    // A pipeline decoded from a graph saved before this slice has no ids/order on any of its steps (today's
    // shape everywhere except a freshly nested chain) — the tree UI needs every step addressable by id, so a
    // step missing one gets a local id, and a step missing `order` gets one scoped to its own (parentStepId,
    // branch) lane, assigned in the list's existing relative order. A chain that already has ids passes through
    // unchanged.
    private fun backfillIds(steps: List<PipelineStep>): List<PipelineStep> {
        if (steps.all { it.id != null }) return steps
        val nextOrderInLane: MutableMap<Pair<String?, String?>, Int> = mutableMapOf()
        return steps.map { step ->
            val lane: Pair<String?, String?> = step.parentStepId to step.branch
            val order: Int = step.order ?: (nextOrderInLane.getOrDefault(lane, 0))
            nextOrderInLane[lane] = order + 1
            step.copy(id = step.id ?: newLocalStepId(), order = order)
        }
    }

    // Swaps [stepId]'s `order` with the sibling [offset] positions away IN THE SAME LANE (same parentStepId +
    // branch) — every other step, including ones in a different lane, keeps its order untouched.
    private fun swapWithLaneSibling(stepId: String, offset: Int) =
        mutateChain { current ->
            val target: PipelineStep = current.firstOrNull { it.id == stepId } ?: return@mutateChain current
            val lane: List<PipelineStep> =
                current
                    .filter { it.parentStepId == target.parentStepId && it.branch == target.branch }
                    .sortedBy { it.order ?: 0 }
            val index: Int = lane.indexOfFirst { it.id == stepId }
            val swapIndex: Int = index + offset
            if (index < 0 || swapIndex !in lane.indices) return@mutateChain current
            val a: PipelineStep = lane[index]
            val b: PipelineStep = lane[swapIndex]
            current.map { step ->
                when (step.id) {
                    a.id -> step.copy(order = b.order)
                    b.id -> step.copy(order = a.order)
                    else -> step
                }
            }
        }

    // Every id transitively parented under [rootId] — walks both lanes of every nested block so removing a
    // block removes its whole subtree, not just its own row.
    private fun descendantIds(steps: List<PipelineStep>, rootId: String): Set<String> {
        val direct: List<String> = steps.filter { it.parentStepId == rootId }.mapNotNull { it.id }
        return direct.toSet() + direct.flatMap { descendantIds(steps, it) }
    }

    // Renumbers just the (parentStepId, branch) lane's `order` values to a dense 0..n-1 run, preserving the
    // lane's existing relative order. Every step outside that lane passes through unchanged.
    private fun reindexLane(steps: List<PipelineStep>, parentStepId: String?, branch: String?): List<PipelineStep> {
        val lane: List<PipelineStep> =
            steps.filter { it.parentStepId == parentStepId && it.branch == branch }.sortedBy { it.order ?: 0 }
        val reindexed: Map<String, Int> = lane.mapIndexedNotNull { index, step -> step.id?.let { it to index } }.toMap()
        return steps.map { step -> reindexed[step.id]?.let { step.copy(order = it) } ?: step }
    }

    /** Persist the edited chain to the backend, then re-fetch the pipeline so the editor shows the saved truth. */
    suspend fun saveChain() {
        val channel: String = channelId ?: return failEdit(NoChannelError)
        val editing: PipelinesState.Editing = _state.value as? PipelinesState.Editing ?: return
        val graph: JsonObject = PipelineGraph(editing.steps).toJson()

        when (
            val result: ApiResult<Unit> =
                pipelinesApi.update(channel, editing.pipelineId, UpdatePipelineBody(graph = graph))
        ) {
            is ApiResult.Failure -> failEdit(result.error.message)
            is ApiResult.Ok -> {
                feedback.success(Res.string.feedback_pipeline_saved)
                // Re-fetch so the editor reflects exactly what was stored (the engine's canonical decode).
                refetchEditing(channel, editing.pipelineId)
            }
        }
    }

    /**
     * Dry-run the open pipeline with sample [variables] (S047). Effects are captured, never performed (backend
     * enforces this). Surfaces the captured result — chat output + effects — inline over the editor, or the
     * failure reason. Only applies while the editor is open.
     */
    suspend fun testRun(variables: Map<String, String>) {
        val channel: String = channelId ?: return failEdit(NoChannelError)
        val editing: PipelinesState.Editing = _state.value as? PipelinesState.Editing ?: return
        val pipelineId: String = editing.pipelineId
        _state.value = editing.copy(testRunning = true, testError = null)

        when (
            val result: ApiResult<TestRunResult> =
                pipelinesApi.testRun(channel, pipelineId, PipelineTestRunBody(variables))
        ) {
            is ApiResult.Ok -> updateEditing(pipelineId) { it.copy(testRunning = false, testResult = result.value, testError = null) }
            is ApiResult.Failure -> updateEditing(pipelineId) { it.copy(testRunning = false, testError = result.error.message) }
        }
    }

    // Apply [transform] to the open editor state only if it is still the same pipeline (guards against the
    // user closing/switching pipelines mid-run).
    private fun updateEditing(pipelineId: String, transform: (PipelinesState.Editing) -> PipelinesState.Editing) {
        val current: PipelinesState = _state.value
        if (current is PipelinesState.Editing && current.pipelineId == pipelineId) {
            _state.value = transform(current)
        }
    }

    // ── internals ────────────────────────────────────────────────────────────

    private suspend fun refetchEditing(channel: String, id: String) {
        // Reuse the palette + picker options already resolved for the open editor; only the chain is re-fetched.
        val current: PipelinesState.Editing? = _state.value as? PipelinesState.Editing
        val options: EditorOptions = current?.options ?: EditorOptions()
        when (val result: ApiResult<PipelineDetail> = pipelinesApi.get(channel, id)) {
            is ApiResult.Failure -> failEdit(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    PipelinesState.Editing(
                        pipelineId = result.value.id,
                        name = result.value.name,
                        steps = backfillIds(result.value.chain.steps),
                        palette = palette,
                        options = options,
                    )
        }
    }

    // Apply an in-memory chain transform while editing; a no-op outside the editor.
    private fun mutateChain(transform: (List<PipelineStep>) -> List<PipelineStep>) {
        val editing: PipelinesState.Editing = _state.value as? PipelinesState.Editing ?: return
        _state.value = editing.copy(steps = transform(editing.steps), actionError = null)
    }

    // A list write either re-lists AND announces success, or surfaces its error over the current list without
    // losing it. [success] lets a delete say "Deleted" while the rest default to "Saved".
    private suspend fun afterListWrite(
        result: ApiResult<Unit>,
        success: org.jetbrains.compose.resources.StringResource = Res.string.feedback_pipeline_saved,
    ) {
        when (result) {
            is ApiResult.Ok -> {
                feedback.success(success)
                channelId?.let { loadList(it) }
            }
            is ApiResult.Failure -> failList(result.error.message)
        }
    }

    private fun failList(detail: String) {
        feedback.error(Res.string.feedback_pipeline_save_failed, detail)
        val current: PipelinesState = _state.value
        _state.value =
            if (current is PipelinesState.Ready) current.copy(actionError = detail)
            else PipelinesState.Error(detail)
    }

    private fun failEdit(detail: String) {
        feedback.error(Res.string.feedback_pipeline_save_failed, detail)
        val current: PipelinesState = _state.value
        if (current is PipelinesState.Editing) _state.value = current.copy(actionError = detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

/** The Pipelines page render state — the list surface and the chain-editor surface. */
sealed interface PipelinesState {
    data object Loading : PipelinesState

    /**
     * The channel's pipelines are listed. [actionError] is non-null only when the last create/rename/toggle/
     * delete failed — the screen surfaces it as a banner while keeping the list rendered.
     */
    data class Ready(val pipelines: List<PipelineSummary>, val actionError: String? = null) :
        PipelinesState

    data object Empty : PipelinesState

    /**
     * Editing one pipeline's action chain: the [pipelineId] the save targets, the pipeline's [name] (shown in
     * the editor header), the ordered [steps] being edited in memory, the backend-sourced block [palette] the
     * step dialog offers, the cross-feature picker [options] (outbound endpoints / pick-lists), and an
     * [actionError] when the last save failed (kept over the edited chain so unsaved work is not lost).
     * [testRunning]/[testResult]/[testError] track the S047 dry-run (Test button): the backend runs the saved
     * chain for real but CAPTURES every side-effecting action instead of performing it.
     */
    data class Editing(
        val pipelineId: String,
        val name: String,
        val steps: List<PipelineStep>,
        val palette: RuntimePalette,
        val options: EditorOptions = EditorOptions(),
        val actionError: String? = null,
        val testRunning: Boolean = false,
        val testResult: TestRunResult? = null,
        val testError: String? = null,
    ) : PipelinesState

    data class Error(val detail: String) : PipelinesState
}

/** One entry in a builder dropdown: the [value] written into the param, and the [label] shown to the user. */
data class PickerOption(val value: String, val label: String)

/**
 * Encodes a "loop" block's `blockConfig` exactly as the engine's `LoopBlockConfig` reads it
 * (PipelineTreeTypes.cs:137): `mode` ("repeat"/"foreach"/"while"), `count` (repeat), `list_var` (foreach),
 * `max_iterations`, `max_loop_runtime_seconds`. Fields that don't apply to [mode] are simply omitted rather
 * than written as null/zero, so a "repeat" block's config never carries a stray `list_var`.
 */
fun encodeLoopConfig(
    mode: String,
    count: Int?,
    listVar: String?,
    maxIterations: Int?,
    maxLoopRuntimeSeconds: Int?,
): JsonElement {
    val map: MutableMap<String, JsonElement> = mutableMapOf("mode" to JsonPrimitive(mode))
    if (mode == "repeat") count?.let { map["count"] = JsonPrimitive(it) }
    if (mode == "foreach") listVar?.let { map["list_var"] = JsonPrimitive(it) }
    maxIterations?.let { map["max_iterations"] = JsonPrimitive(it) }
    maxLoopRuntimeSeconds?.let { map["max_loop_runtime_seconds"] = JsonPrimitive(it) }
    return JsonObject(map)
}

/** Reads a "loop" block's `mode`/`count`/`list_var`/`max_iterations`/`max_loop_runtime_seconds` back out of
 * its `blockConfig` (the mirror of [encodeLoopConfig]), defaulting `mode` to "repeat" as the engine does
 * (`config.Mode ?? "repeat"`, PipelineEngine.cs:1740) when the field is absent. */
fun decodeLoopConfig(blockConfig: JsonElement?): LoopConfigFields {
    val obj: JsonObject? = blockConfig as? JsonObject
    return LoopConfigFields(
        mode = obj?.get("mode")?.jsonPrimitive?.contentOrNull ?: "repeat",
        count = obj?.get("count")?.jsonPrimitive?.intOrNull,
        listVar = obj?.get("list_var")?.jsonPrimitive?.contentOrNull,
        maxIterations = obj?.get("max_iterations")?.jsonPrimitive?.intOrNull,
        maxLoopRuntimeSeconds = obj?.get("max_loop_runtime_seconds")?.jsonPrimitive?.intOrNull,
    )
}

/** Decoded shape of a "loop" block's `blockConfig` — see [decodeLoopConfig]. */
data class LoopConfigFields(
    val mode: String,
    val count: Int?,
    val listVar: String?,
    val maxIterations: Int?,
    val maxLoopRuntimeSeconds: Int?,
)

/**
 * Builds a [PickerOption] for a cross-feature dropdown, routing the label through [resolveRowLabel]
 * so a blank-named entity (name/title empty or null from the backend) never renders as an unreadable
 * blank row a user could still select — it falls back to a typed placeholder discriminated by [id].
 */
private fun labeledOption(id: String, name: String?, typeLabel: String): PickerOption =
    PickerOption(value = id, label = resolveRowLabel(name, typeLabel = typeLabel, discriminatorSource = id))

/**
 * The cross-feature dropdown sources the chain editor offers for specific fields: the channel's outbound
 * webhook [outboundEndpoints] (the `send_webhook` block's endpoint picker) and [pickLists] (the
 * `pick_from_list` block's list picker). Empty lists mean the field falls back to free-text entry.
 */
data class EditorOptions(
    val outboundEndpoints: List<PickerOption> = emptyList(),
    val pickLists: List<PickerOption> = emptyList(),
    /** The channel's overlay widgets — the `widget_event` block's `widget_id` picker (value = widget id). */
    val widgets: List<PickerOption> = emptyList(),
    /** The channel's pipelines by NAME — the `schedule_pipeline`/`run_pipeline` blocks' `pipeline` picker (value = name). */
    val pipelines: List<PickerOption> = emptyList(),
    /**
     * Each pipeline's declared sub-pipeline parameter names (S-PIPE-TREE-d2b-UI), keyed by pipeline NAME (the
     * same key space as [pipelines]) — only pipelines that declare at least one name appear. The `run_pipeline`
     * block's argument editor looks up the currently-picked target here to decide whether to render one
     * labelled field per declared name or fall back to the generic positional `args` editor.
     */
    val pipelineParameterNames: Map<String, List<String>> = emptyMap(),
    /** The channel's sound clips — the `play_sound` block's `clip` picker (value = clip id). */
    val soundClips: List<PickerOption> = emptyList(),
    /** The channel's TTS voices — the `play_tts` block's `voice` picker (value = voice id). */
    val ttsVoices: List<PickerOption> = emptyList(),
    /** The channel's savings jars — the `jar_contribute` block's `jar_id` picker (value = jar id). */
    val jars: List<PickerOption> = emptyList(),
    /** The channel's code scripts — the `run_code` block's `code_script_id` picker (value = script id). */
    val codeScripts: List<PickerOption> = emptyList(),
    /** The channel's giveaways — the giveaway blocks' `giveaway_id` picker (value = giveaway id). */
    val giveaways: List<PickerOption> = emptyList(),
    /** The channel's quotes by NUMBER — the `post_quote` block's `quote_number` picker (value = number). */
    val quotes: List<PickerOption> = emptyList(),
    /**
     * The backend's rich resource-picker option supply (S-RICH-PICKERS, `GET pipelines/options/{kind}`) — a
     * field whose [BlockField.remoteKind] names a [bot.nomnomz.dashboard.core.network.PickerKind] fetches its
     * own option page directly through this on open/search rather than a preloaded list, since these sources
     * (Discord roles, Twitch users, assets, …) can be large and support server-side search. Null when the API
     * is unavailable — the field then degrades to the legacy free-text/local-list entry for that key, same as
     * every other optional editor source above.
     */
    val pipelineOptionsApi: PipelineOptionsApi? = null,
)
