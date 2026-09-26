// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.picklists.state

import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.InstallPlatformTemplateBody
import bot.nomnomz.dashboard.core.network.InstalledPlatformTemplate
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.PlatformTemplate
import bot.nomnomz.dashboard.core.network.PlatformTemplatesApi
import nomnomzbot.composeapp.generated.resources.platform_templates_installed
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.CreatePickListBody
import bot.nomnomz.dashboard.core.network.PickList
import bot.nomnomz.dashboard.core.network.PickListsApi
import bot.nomnomz.dashboard.core.network.UpdatePickListBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_picklist_deleted
import nomnomzbot.composeapp.generated.resources.feedback_picklist_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_picklist_saved
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary

// Proves the Pick Lists page state machine the screen renders: surface the channel's real pick-lists — empty when
// there are none, error if the list call fails — and follow through on every write (create / edit / delete) by
// re-listing so the consequence is observed (a new row, replaced entries, a removed row), not merely that a call
// happened. It also proves the wire-shaping the controller owns: the name is trimmed, a blank description becomes
// null, and blank entries are dropped before they reach the api. The screen is a pure projection of this.
class PickListsControllerTest {

    @Test
    fun templates_lists_the_pick_list_kind_for_the_active_channel() = runTest {
        val templatesApi = RecordingPickListTemplatesApi()
        val controller =
            pickListsController(RecordingPickListsApi(ApiResult.Ok(emptyList())), templatesApi = templatesApi)

        val result: ApiResult<List<PlatformTemplate>> = controller.templates()

        assertEquals("def-greetings", (result as ApiResult.Ok).value.single().definitionId)
        assertEquals("ch1" to "pick_list", templatesApi.lastListed)
    }

    @Test
    fun installTemplate_installs_into_the_active_channel_then_reloads_and_confirms() = runTest {
        val templatesApi = RecordingPickListTemplatesApi()
        val feedback = RecordingFeedback()
        val api = RecordingPickListsApi(ApiResult.Ok(emptyList()))
        val controller = pickListsController(api, feedback, templatesApi = templatesApi)

        val result: ApiResult<InstalledPlatformTemplate> = controller.installTemplate(GreetingsTemplate)

        assertEquals("greetings", (result as ApiResult.Ok).value.name)
        assertEquals("ch1" to "def-greetings", templatesApi.lastInstalled)
        assertTrue(controller.state.value is PickListsState.Empty)
        assertEquals(Res.string.platform_templates_installed, feedback.only.label)
    }

    @Test
    fun installTemplate_refusal_is_returned_without_a_success_toast() = runTest {
        val templatesApi =
            RecordingPickListTemplatesApi(
                installResult =
                    ApiResult.Failure(ApiError(409, "ALREADY_EXISTS", "A pick list named 'greetings' already exists."))
            )
        val feedback = RecordingFeedback()
        val controller =
            pickListsController(RecordingPickListsApi(ApiResult.Ok(emptyList())), feedback, templatesApi = templatesApi)

        val result: ApiResult<InstalledPlatformTemplate> = controller.installTemplate(GreetingsTemplate)

        assertEquals("ALREADY_EXISTS", (result as ApiResult.Failure).error.code)
        assertTrue(feedback.messages.isEmpty())
    }

    @Test
    fun load_surfaces_the_channel_lists_on_success() = runTest {
        val controller =
            pickListsController(
                RecordingPickListsApi(
                    ApiResult.Ok(
                        listOf(
                            PickList(
                                id = "pl1",
                                name = "fight_moves",
                                description = "Attack phrases for !fight",
                                items = listOf("throws a chair", "lands a jab"),
                                createdAt = "2026-06-01T12:00:00Z",
                                updatedAt = "2026-06-01T12:00:00Z",
                            )
                        )
                    )
                )
            )

        controller.load()

        val state: PickListsState = controller.state.value
        assertTrue(state is PickListsState.Ready)
        val lists: List<PickList> = (state as PickListsState.Ready).lists
        assertEquals(1, lists.size)
        val list: PickList = lists.first()
        assertEquals("fight_moves", list.name)
        assertEquals("Attack phrases for !fight", list.description)
        assertEquals(listOf("throws a chair", "lands a jab"), list.items)
    }

    @Test
    fun load_is_empty_when_the_channel_has_no_lists() = runTest {
        val controller = pickListsController(RecordingPickListsApi(ApiResult.Ok(emptyList())))

        controller.load()

        assertTrue(controller.state.value is PickListsState.Empty)
    }

    @Test
    fun load_errors_when_the_list_call_fails() = runTest {
        val controller =
            pickListsController(RecordingPickListsApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))))

        controller.load()

        val state: PickListsState = controller.state.value
        assertTrue(state is PickListsState.Error)
        assertEquals("boom", (state as PickListsState.Error).detail)
    }

    @Test
    fun create_posts_the_cleaned_body_then_reloads_with_the_new_list() = runTest {
        // The fake starts empty; the create appends the new list to its backing store, so the controller's
        // post-write reload must surface it — proving create actually calls the api AND re-lists.
        val api = RecordingPickListsApi(ApiResult.Ok(emptyList()))
        val controller = pickListsController(api)
        controller.load()
        assertTrue(controller.state.value is PickListsState.Empty)

        controller.createPickList(
            name = "  fight_moves  ",
            description = "Attack phrases",
            items = listOf(" throws a chair ", "", "  ", "lands a jab"),
        )

        // The api recorded exactly the body the controller built: the name trimmed and the entries trimmed with the
        // blank ones dropped — a blank line the operator left in the editor is not a pickable entry.
        assertEquals(1, api.created.size)
        val body: CreatePickListBody = api.created.first()
        assertEquals("fight_moves", body.name)
        assertEquals("Attack phrases", body.description)
        assertEquals(listOf("throws a chair", "lands a jab"), body.items)

        // And the reload surfaced the freshly-created row.
        val state: PickListsState = controller.state.value
        assertTrue(state is PickListsState.Ready)
        val lists: List<PickList> = (state as PickListsState.Ready).lists
        assertEquals(1, lists.size)
        assertEquals("fight_moves", lists.first().name)
        assertEquals(listOf("throws a chair", "lands a jab"), lists.first().items)
    }

    @Test
    fun create_sends_a_blank_description_as_null() = runTest {
        // A blank description is meaningless: it must go over the wire as null (omitted), not as an empty string.
        val api = RecordingPickListsApi(ApiResult.Ok(emptyList()))
        val controller = pickListsController(api)
        controller.load()

        controller.createPickList(name = "greetings", description = "   ", items = listOf("hi"))

        val body: CreatePickListBody = api.created.first()
        assertEquals("greetings", body.name)
        assertNull(body.description)
        assertEquals(listOf("hi"), body.items)
    }

    @Test
    fun edit_puts_the_new_state_by_id_then_reloads_with_the_change() = runTest {
        val api =
            RecordingPickListsApi(
                ApiResult.Ok(
                    listOf(PickList(id = "pl5", name = "old_name", items = listOf("one")))
                )
            )
        val controller = pickListsController(api)
        controller.load()

        controller.updatePickList(
            id = "pl5",
            name = "new_name",
            description = "now described",
            items = listOf("one", "two"),
        )

        // The edit is a PUT addressed by the opaque id, carrying the desired full state (renamed, new entries).
        assertEquals(1, api.updated.size)
        val update: Pair<String, UpdatePickListBody> = api.updated.first()
        assertEquals("pl5", update.first)
        assertEquals("new_name", update.second.name)
        assertEquals("now described", update.second.description)
        assertEquals(listOf("one", "two"), update.second.items)

        // The reload reflects the persisted edit.
        val state: PickListsState = controller.state.value
        assertTrue(state is PickListsState.Ready)
        val list: PickList = (state as PickListsState.Ready).lists.first()
        assertEquals("new_name", list.name)
        assertEquals(listOf("one", "two"), list.items)
    }

    @Test
    fun delete_removes_the_list_then_reloads_to_empty() = runTest {
        val api =
            RecordingPickListsApi(ApiResult.Ok(listOf(PickList(id = "pl9", name = "bye", items = listOf("x")))))
        val controller = pickListsController(api)
        controller.load()
        assertTrue(controller.state.value is PickListsState.Ready)

        controller.deletePickList(id = "pl9")

        assertEquals(listOf("pl9"), api.deleted)
        // The store is now empty, so the post-delete reload lands on Empty — the row is really gone.
        assertTrue(controller.state.value is PickListsState.Empty)
    }

    @Test
    fun a_failed_write_keeps_the_list_and_announces_on_the_feedback_toast() = runTest {
        val api =
            RecordingPickListsApi(
                ApiResult.Ok(listOf(PickList(id = "pl1", name = "keep_me", items = listOf("x")))),
                writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
            )
        val feedback = RecordingFeedback()
        val controller = pickListsController(api, feedback)
        controller.load()

        controller.deletePickList(id = "pl1")

        // The list is kept (not blown away) and the failure announces on the shell-level feedback toast.
        val state: PickListsState = controller.state.value
        assertTrue(state is PickListsState.Ready)
        assertEquals(1, (state as PickListsState.Ready).lists.size)
        assertEquals("keep_me", state.lists.first().name)
        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(listOf("no permission"), feedback.only.formatArgs)
    }

    @Test
    fun a_successful_edit_announces_save_success_on_the_frame() = runTest {
        val feedback = RecordingFeedback()
        val controller =
            pickListsController(
                RecordingPickListsApi(ApiResult.Ok(listOf(PickList(id = "pl2", name = "hi", items = listOf("x"))))),
                feedback,
            )
        controller.load()

        controller.updatePickList(id = "pl2", name = "hello", description = null, items = listOf("x"))

        // The edit (a save) announced exactly one success with the "saved" label.
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_picklist_saved, feedback.only.label)
    }

    @Test
    fun a_successful_delete_announces_the_deleted_label() = runTest {
        val feedback = RecordingFeedback()
        val controller =
            pickListsController(
                RecordingPickListsApi(ApiResult.Ok(listOf(PickList(id = "pl4", name = "del_me", items = listOf("x"))))),
                feedback,
            )
        controller.load()

        controller.deletePickList(id = "pl4")

        // A delete says "deleted", not the generic "saved" — the success message is action-specific.
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_picklist_deleted, feedback.only.label)
    }

    @Test
    fun a_failed_write_announces_an_error_carrying_the_backend_detail() = runTest {
        val feedback = RecordingFeedback()
        val controller =
            pickListsController(
                RecordingPickListsApi(
                    ApiResult.Ok(listOf(PickList(id = "pl7", name = "boom", items = listOf("x")))),
                    writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
                ),
                feedback,
            )
        controller.load()

        controller.deletePickList(id = "pl7")

        // The failure path emits an ERROR (never a success), carrying the backend message as the detail arg.
        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(Res.string.feedback_picklist_save_failed, feedback.only.label)
        assertEquals(listOf<Any>("no permission"), feedback.only.formatArgs)
    }
}

// A recording fake that behaves like the backend store: list() returns the live store, and each successful write
// mutates the store so the controller's post-write reload observes the real consequence (a new row, replaced
// entries, a removed row) — not merely that a call happened. [writeResult] forces every write to fail (the store
// is left untouched) to exercise the error path. A list-level failure is modelled by passing a Failure as the
// initial result.
private class RecordingPickListsApi(
    initial: ApiResult<List<PickList>>,
    private val writeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : PickListsApi {
    // Not exercised here: the counted delete preview has its own tests (DeleteBlastRadiusDialogTest and the
    // backend's blast-radius suites). The seam is implemented so the double stays a real implementation.
    override suspend fun blastRadius(id: String): ApiResult<BlastRadiusSummary> =
        ApiResult.Ok(BlastRadiusSummary())

    private val listFailure: ApiError? = (initial as? ApiResult.Failure)?.error
    private val store: MutableList<PickList> =
        (initial as? ApiResult.Ok)?.value?.toMutableList() ?: mutableListOf()

    val created: MutableList<CreatePickListBody> = mutableListOf()
    val updated: MutableList<Pair<String, UpdatePickListBody>> = mutableListOf()
    val deleted: MutableList<String> = mutableListOf()

    override suspend fun list(): ApiResult<List<PickList>> =
        listFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(store.toList())

    override suspend fun get(id: String): ApiResult<PickList> =
        store.firstOrNull { it.id == id }?.let { ApiResult.Ok(it) }
            ?: ApiResult.Failure(ApiError(404, "NOT_FOUND", "no such list"))

    override suspend fun create(body: CreatePickListBody): ApiResult<Unit> {
        created += body
        if (writeResult is ApiResult.Ok) {
            val nextId: String = "pl${store.size + 1}"
            store +=
                PickList(
                    id = nextId,
                    name = body.name,
                    description = body.description,
                    items = body.items,
                )
        }
        return writeResult
    }

    override suspend fun update(id: String, body: UpdatePickListBody): ApiResult<Unit> {
        updated += id to body
        if (writeResult is ApiResult.Ok) {
            val index: Int = store.indexOfFirst { it.id == id }
            if (index >= 0) {
                store[index] =
                    store[index].copy(
                        name = body.name,
                        description = body.description,
                        items = body.items,
                    )
            }
        }
        return writeResult
    }

    override suspend fun delete(id: String): ApiResult<Unit> {
        deleted += id
        if (writeResult is ApiResult.Ok) {
            store.removeAll { it.id == id }
        }
        return writeResult
    }

    override suspend fun pick(id: String): ApiResult<bot.nomnomz.dashboard.core.network.PickListPreview> {
        val list: PickList? = store.firstOrNull { it.id == id }
        val entry: String = list?.items?.firstOrNull().orEmpty()
        return ApiResult.Ok(bot.nomnomz.dashboard.core.network.PickListPreview(pick = entry))
    }
}

private fun pickListsController(
    api: PickListsApi,
    feedback: Feedback = NoOpFeedback,
    templatesApi: PlatformTemplatesApi = RecordingPickListTemplatesApi(),
): PickListsController = PickListsController(api, ActiveChannelApi, templatesApi, feedback)

private object ActiveChannelApi : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = ApiResult.Ok(ChannelSummary(id = "ch1"))
    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())
    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}

private class RecordingPickListTemplatesApi(
    private val installResult: ApiResult<InstalledPlatformTemplate> =
        ApiResult.Ok(InstalledPlatformTemplate(kind = "pick_list", entityId = "pl9", name = "greetings")),
) : PlatformTemplatesApi {
    var lastListed: Pair<String, String>? = null
    var lastInstalled: Pair<String, String>? = null

    override suspend fun list(channelId: String, kind: String): ApiResult<List<PlatformTemplate>> {
        lastListed = channelId to kind
        return ApiResult.Ok(listOf(GreetingsTemplate))
    }

    override suspend fun install(
        channelId: String,
        definitionId: String,
        body: InstallPlatformTemplateBody,
    ): ApiResult<InstalledPlatformTemplate> {
        lastInstalled = channelId to definitionId
        return installResult
    }
}

private val GreetingsTemplate: PlatformTemplate =
    PlatformTemplate(
        definitionId = "def-greetings",
        kind = "pick_list",
        key = "greetings",
        displayName = "Greetings",
        version = 1,
        payloadJson = """{"name":"greetings","items":["Hey {user}!"]}""",
    )
