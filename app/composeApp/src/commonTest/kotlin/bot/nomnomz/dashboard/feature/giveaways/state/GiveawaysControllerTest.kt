// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.giveaways.state

import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.AddCodesBody
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.CodePool
import bot.nomnomz.dashboard.core.network.CodePoolDetail
import bot.nomnomz.dashboard.core.network.CreateCodePoolBody
import bot.nomnomz.dashboard.core.network.Giveaway
import bot.nomnomz.dashboard.core.network.GiveawayCodeStatus
import bot.nomnomz.dashboard.core.network.GiveawayEntryMode
import bot.nomnomz.dashboard.core.network.GiveawayStatus
import bot.nomnomz.dashboard.core.network.GiveawayWinner
import bot.nomnomz.dashboard.core.network.GiveawayWinnerStatus
import bot.nomnomz.dashboard.core.network.GiveawaysApi
import bot.nomnomz.dashboard.core.network.MaskedCode
import bot.nomnomz.dashboard.core.network.UpsertGiveawayBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_giveaway_deleted
import nomnomzbot.composeapp.generated.resources.feedback_giveaway_drawn
import nomnomzbot.composeapp.generated.resources.feedback_giveaway_opened
import nomnomzbot.composeapp.generated.resources.feedback_giveaway_save_failed

// Proves the Giveaways page state machine the screen renders: surface the channel's real campaigns (empty when
// there are none, error if the list call fails), and follow through on every action by observing its CONSEQUENCE
// — a created row appears, open/close flip the status, a draw produces winners AND opens the winner panel, a
// redraw replaces a winner, a reveal puts the plaintext on the panel, delete removes the row, and a failed write
// is surfaced over the kept list. The code-pool surface is proven the same way. The screen is a pure projection
// of this, so testing the holder proves the behaviour without rendering Compose.
class GiveawaysControllerTest {

    @Test
    fun load_surfaces_the_channel_giveaways_on_success() = runTest {
        val controller =
            GiveawaysController(
                RecordingGiveawaysApi(
                    ApiResult.Ok(
                        listOf(
                            Giveaway(
                                id = "g1",
                                title = "Nitro drop",
                                entryMode = GiveawayEntryMode.Keyword,
                                keyword = "!drop",
                                status = GiveawayStatus.Open,
                                entryCount = 42,
                            )
                        )
                    )
                )
            )

        controller.load()

        val state: GiveawaysState = controller.state.value
        assertTrue(state is GiveawaysState.Ready)
        val giveaway: Giveaway = (state as GiveawaysState.Ready).giveaways.single()
        assertEquals("Nitro drop", giveaway.title)
        assertEquals(GiveawayStatus.Open, giveaway.status)
        assertEquals(42, giveaway.entryCount)
        assertNull(state.actionError)
    }

    @Test
    fun load_is_empty_when_the_channel_has_no_giveaways() = runTest {
        val controller = GiveawaysController(RecordingGiveawaysApi(ApiResult.Ok(emptyList())))

        controller.load()

        assertTrue(controller.state.value is GiveawaysState.Empty)
    }

    @Test
    fun load_errors_when_the_list_call_fails() = runTest {
        val controller =
            GiveawaysController(RecordingGiveawaysApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))))

        controller.load()

        val state: GiveawaysState = controller.state.value
        assertTrue(state is GiveawaysState.Error)
        assertEquals("boom", (state as GiveawaysState.Error).detail)
    }

    @Test
    fun create_posts_the_body_then_reloads_with_the_new_row() = runTest {
        val api = RecordingGiveawaysApi(ApiResult.Ok(emptyList()))
        val controller = GiveawaysController(api)
        controller.load()
        assertTrue(controller.state.value is GiveawaysState.Empty)

        controller.createGiveaway(
            UpsertGiveawayBody(
                title = "Keyboard giveaway",
                entryMode = GiveawayEntryMode.Keyword,
                keyword = "!win",
                winnerCount = 2,
            )
        )

        // The api recorded exactly the body the controller passed, AND the post-write reload surfaced the new row.
        val body: UpsertGiveawayBody = api.created.single()
        assertEquals("Keyboard giveaway", body.title)
        assertEquals(GiveawayEntryMode.Keyword, body.entryMode)
        assertEquals(2, body.winnerCount)

        val state: GiveawaysState = controller.state.value
        assertTrue(state is GiveawaysState.Ready)
        val created: Giveaway = (state as GiveawaysState.Ready).giveaways.single()
        assertEquals("Keyboard giveaway", created.title)
        assertEquals(GiveawayStatus.Draft, created.status)
    }

    @Test
    fun open_flips_the_status_to_open_and_announces_it() = runTest {
        val feedback = RecordingFeedback()
        val api = RecordingGiveawaysApi(ApiResult.Ok(listOf(draft("g1"))))
        val controller = GiveawaysController(api, feedback)
        controller.load()

        controller.openGiveaway("g1")

        assertEquals(listOf("g1"), api.opened)
        val giveaway: Giveaway = readyGiveaways(controller).single()
        assertEquals(GiveawayStatus.Open, giveaway.status)
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_giveaway_opened, feedback.only.label)
    }

    @Test
    fun close_flips_an_open_giveaway_to_closed() = runTest {
        val api = RecordingGiveawaysApi(ApiResult.Ok(listOf(draft("g1").copy(status = GiveawayStatus.Open))))
        val controller = GiveawaysController(api)
        controller.load()

        controller.closeGiveaway("g1")

        assertEquals(listOf("g1"), api.closed)
        assertEquals(GiveawayStatus.Closed, readyGiveaways(controller).single().status)
    }

    @Test
    fun draw_returns_winners_reloads_as_drawn_and_opens_the_winner_panel() = runTest {
        val feedback = RecordingFeedback()
        val closed: Giveaway = draft("g1").copy(status = GiveawayStatus.Closed)
        val winners: List<GiveawayWinner> =
            listOf(winner("w1", "g1", "alice"), winner("w2", "g1", "bob"))
        val api = RecordingGiveawaysApi(ApiResult.Ok(listOf(closed)), drawWinners = winners)
        val controller = GiveawaysController(api, feedback)
        controller.load()

        controller.drawGiveaway(closed)

        // The list reloaded with the giveaway now DRAWN...
        assertEquals(GiveawayStatus.Drawn, readyGiveaways(controller).single().status)
        // ...the winner panel opened on exactly the two drawn winners...
        val panel: WinnersState = controller.winners.value
        assertTrue(panel is WinnersState.Ready)
        assertEquals(listOf("alice", "bob"), (panel as WinnersState.Ready).winners.map { it.viewerDisplayName })
        // ...and the draw announced its own outcome.
        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_giveaway_drawn, feedback.only.label)
    }

    @Test
    fun show_winners_loads_the_history_into_the_panel() = runTest {
        val g: Giveaway = draft("g1").copy(status = GiveawayStatus.Drawn)
        val api = RecordingGiveawaysApi(ApiResult.Ok(listOf(g)))
        api.seedWinners("g1", listOf(winner("w1", "g1", "carol")))
        val controller = GiveawaysController(api)
        controller.load()

        controller.showWinners(g)

        val panel: WinnersState = controller.winners.value
        assertTrue(panel is WinnersState.Ready)
        assertEquals("carol", (panel as WinnersState.Ready).winners.single().viewerDisplayName)

        controller.hideWinners()
        assertTrue(controller.winners.value is WinnersState.Hidden)
    }

    @Test
    fun redraw_marks_the_old_winner_redrawn_and_appends_a_replacement() = runTest {
        val g: Giveaway = draft("g1").copy(status = GiveawayStatus.Drawn)
        val api = RecordingGiveawaysApi(ApiResult.Ok(listOf(g)))
        api.seedWinners("g1", listOf(winner("w1", "g1", "dave")))
        val controller = GiveawaysController(api)
        controller.load()
        controller.showWinners(g)

        controller.redrawWinner(g, "w1")

        val panel: WinnersState = controller.winners.value
        assertTrue(panel is WinnersState.Ready)
        val list: List<GiveawayWinner> = (panel as WinnersState.Ready).winners
        assertEquals(2, list.size)
        assertEquals(GiveawayWinnerStatus.Redrawn, list.first { it.id == "w1" }.status)
        assertTrue(list.any { it.isRedraw }, "a fresh replacement winner is appended")
    }

    @Test
    fun reveal_code_puts_the_plaintext_on_the_open_panel_keyed_by_winner() = runTest {
        val g: Giveaway = draft("g1").copy(status = GiveawayStatus.Drawn)
        val api =
            RecordingGiveawaysApi(ApiResult.Ok(listOf(g)), revealResult = ApiResult.Ok("PROMO-SECRET-9"))
        api.seedWinners("g1", listOf(winner("w1", "g1", "erin").copy(assignedCodeId = "c1", whisperDelivered = false)))
        val controller = GiveawaysController(api)
        controller.load()
        controller.showWinners(g)

        controller.revealCode(g, "w1")

        val panel: WinnersState = controller.winners.value
        assertTrue(panel is WinnersState.Ready)
        assertEquals("PROMO-SECRET-9", (panel as WinnersState.Ready).revealedCodes["w1"])
    }

    @Test
    fun delete_removes_the_giveaway_then_reloads_to_empty() = runTest {
        val feedback = RecordingFeedback()
        val api = RecordingGiveawaysApi(ApiResult.Ok(listOf(draft("g9"))))
        val controller = GiveawaysController(api, feedback)
        controller.load()
        assertTrue(controller.state.value is GiveawaysState.Ready)

        controller.deleteGiveaway("g9")

        assertEquals(listOf("g9"), api.deleted)
        assertTrue(controller.state.value is GiveawaysState.Empty)
        assertEquals(Res.string.feedback_giveaway_deleted, feedback.only.label)
    }

    @Test
    fun a_failed_write_surfaces_the_error_over_the_kept_list() = runTest {
        val feedback = RecordingFeedback()
        val api =
            RecordingGiveawaysApi(
                ApiResult.Ok(listOf(draft("g1").copy(title = "keep me"))),
                writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
            )
        val controller = GiveawaysController(api, feedback)
        controller.load()

        controller.openGiveaway("g1")

        // The list is kept (not blown away) and the failure is surfaced on it AND on the frame.
        val state: GiveawaysState = controller.state.value
        assertTrue(state is GiveawaysState.Ready)
        assertEquals("keep me", (state as GiveawaysState.Ready).giveaways.single().title)
        assertEquals("no permission", state.actionError)
        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(Res.string.feedback_giveaway_save_failed, feedback.only.label)
    }

    @Test
    fun code_pools_load_create_and_delete_reflect_the_store() = runTest {
        val api = RecordingGiveawaysApi(ApiResult.Ok(emptyList()), poolsInitial = ApiResult.Ok(emptyList()))
        val controller = GiveawaysController(api)

        controller.loadCodePools()
        assertTrue(controller.codePools.value is CodePoolsState.Empty)

        controller.createCodePool("Steam keys", "GOTY bundle")
        val pools: CodePoolsState = controller.codePools.value
        assertTrue(pools is CodePoolsState.Ready)
        val pool: CodePool = (pools as CodePoolsState.Ready).pools.single()
        assertEquals("Steam keys", pool.name)
        assertEquals("GOTY bundle", pool.description)

        controller.deleteCodePool(pool.id)
        assertTrue(controller.codePools.value is CodePoolsState.Empty)
    }

    @Test
    fun add_codes_trims_and_drops_blanks_and_reloads_the_pool_detail() = runTest {
        val api =
            RecordingGiveawaysApi(
                ApiResult.Ok(emptyList()),
                poolsInitial = ApiResult.Ok(listOf(CodePool(id = "p1", name = "keys", total = 0))),
            )
        val controller = GiveawaysController(api)
        controller.loadCodePools()
        controller.showPoolDetail(CodePool(id = "p1", name = "keys"))

        controller.addCodes("p1", listOf("  ABC-1 ", "", "   ", "ABC-2"))

        // The wire body carried exactly the two non-blank, trimmed codes (label-less) — a blank line is not a code.
        val body: AddCodesBody = api.addedCodes.single { it.first == "p1" }.second
        assertEquals(listOf("ABC-1", "ABC-2"), body.codes.map { it.code })
        assertTrue(body.codes.all { it.label == null })
        // The manage-pool panel reloaded its masked detail (never plaintext).
        val detail: PoolDetailState = controller.poolDetail.value
        assertTrue(detail is PoolDetailState.Ready)
        assertTrue((detail as PoolDetailState.Ready).pool.codes.all { it.status == GiveawayCodeStatus.Available })
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private fun readyGiveaways(controller: GiveawaysController): List<Giveaway> =
        (controller.state.value as GiveawaysState.Ready).giveaways

    private fun draft(id: String): Giveaway =
        Giveaway(id = id, title = "Giveaway $id", entryMode = GiveawayEntryMode.Keyword, status = GiveawayStatus.Draft)

    private fun winner(id: String, giveawayId: String, name: String): GiveawayWinner =
        GiveawayWinner(
            id = id,
            giveawayId = giveawayId,
            viewerUserId = "u-$id",
            viewerDisplayName = name,
            drawnAt = "2026-07-12T12:00:00Z",
            status = GiveawayWinnerStatus.Drawn,
        )
}

// A recording fake that behaves like the backend store: list() returns the live store, and each successful write
// mutates it so the controller's post-write reload observes the real consequence. Lifecycle transitions flip the
// stored status; a draw seeds the winner history; a redraw marks the old winner redrawn and appends a
// replacement. [writeResult] forces every write to fail (the store is left untouched) to exercise the error path.
private class RecordingGiveawaysApi(
    initial: ApiResult<List<Giveaway>>,
    private val poolsInitial: ApiResult<List<CodePool>> = ApiResult.Ok(emptyList()),
    private val writeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val drawWinners: List<GiveawayWinner> = emptyList(),
    private val revealResult: ApiResult<String> = ApiResult.Ok("PLAINTEXT-CODE"),
) : GiveawaysApi {
    private val listFailure: ApiError? = (initial as? ApiResult.Failure)?.error
    private val store: MutableList<Giveaway> =
        (initial as? ApiResult.Ok)?.value?.toMutableList() ?: mutableListOf()
    private val poolsFailure: ApiError? = (poolsInitial as? ApiResult.Failure)?.error
    private val pools: MutableList<CodePool> =
        (poolsInitial as? ApiResult.Ok)?.value?.toMutableList() ?: mutableListOf()
    private val winnersStore: MutableMap<String, MutableList<GiveawayWinner>> = mutableMapOf()

    val created: MutableList<UpsertGiveawayBody> = mutableListOf()
    val opened: MutableList<String> = mutableListOf()
    val closed: MutableList<String> = mutableListOf()
    val drew: MutableList<String> = mutableListOf()
    val deleted: MutableList<String> = mutableListOf()
    val addedCodes: MutableList<Pair<String, AddCodesBody>> = mutableListOf()

    fun seedWinners(giveawayId: String, winners: List<GiveawayWinner>) {
        winnersStore[giveawayId] = winners.toMutableList()
    }

    override suspend fun list(status: String?): ApiResult<List<Giveaway>> =
        listFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(store.toList())

    override suspend fun create(body: UpsertGiveawayBody): ApiResult<Unit> {
        created += body
        if (writeResult is ApiResult.Ok) {
            store +=
                Giveaway(
                    id = "g${store.size + 1}",
                    title = body.title,
                    entryMode = body.entryMode,
                    keyword = body.keyword,
                    winnerCount = body.winnerCount,
                    prizeMode = body.prizeMode,
                    prizeCodePoolId = body.prizeCodePoolId,
                    status = GiveawayStatus.Draft,
                )
        }
        return writeResult
    }

    override suspend fun update(id: String, body: UpsertGiveawayBody): ApiResult<Unit> {
        if (writeResult is ApiResult.Ok) {
            val index: Int = store.indexOfFirst { it.id == id }
            if (index >= 0) store[index] = store[index].copy(title = body.title, keyword = body.keyword)
        }
        return writeResult
    }

    override suspend fun delete(id: String): ApiResult<Unit> {
        deleted += id
        if (writeResult is ApiResult.Ok) store.removeAll { it.id == id }
        return writeResult
    }

    override suspend fun open(id: String): ApiResult<Unit> {
        opened += id
        if (writeResult is ApiResult.Ok) setStatus(id, GiveawayStatus.Open)
        return writeResult
    }

    override suspend fun close(id: String): ApiResult<Unit> {
        closed += id
        if (writeResult is ApiResult.Ok) setStatus(id, GiveawayStatus.Closed)
        return writeResult
    }

    override suspend fun draw(id: String): ApiResult<List<GiveawayWinner>> {
        drew += id
        if (writeResult is ApiResult.Failure) return ApiResult.Failure(writeResult.error)
        setStatus(id, GiveawayStatus.Drawn)
        winnersStore[id] = drawWinners.toMutableList()
        return ApiResult.Ok(drawWinners)
    }

    override suspend fun redraw(id: String, winnerId: String): ApiResult<Unit> {
        if (writeResult is ApiResult.Ok) {
            val list: MutableList<GiveawayWinner> = winnersStore.getOrPut(id) { mutableListOf() }
            val index: Int = list.indexOfFirst { it.id == winnerId }
            if (index >= 0) {
                list[index] = list[index].copy(status = GiveawayWinnerStatus.Redrawn)
                list +=
                    GiveawayWinner(
                        id = "$winnerId-r",
                        giveawayId = id,
                        viewerUserId = "u-replacement",
                        viewerDisplayName = "Replacement",
                        drawnAt = "2026-07-12T12:05:00Z",
                        status = GiveawayWinnerStatus.Drawn,
                        isRedraw = true,
                    )
            }
        }
        return writeResult
    }

    override suspend fun winners(id: String): ApiResult<List<GiveawayWinner>> =
        ApiResult.Ok(winnersStore[id]?.toList() ?: emptyList())

    override suspend fun revealCode(id: String, winnerId: String): ApiResult<String> = revealResult

    override suspend fun listCodePools(): ApiResult<List<CodePool>> =
        poolsFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(pools.toList())

    override suspend fun createCodePool(body: CreateCodePoolBody): ApiResult<Unit> {
        if (writeResult is ApiResult.Ok) {
            pools += CodePool(id = "p${pools.size + 1}", name = body.name, description = body.description)
        }
        return writeResult
    }

    override suspend fun codePool(poolId: String): ApiResult<CodePoolDetail> =
        ApiResult.Ok(
            CodePoolDetail(
                id = poolId,
                name = "keys",
                codes = listOf(MaskedCode(id = "c1", label = "••••1234", status = GiveawayCodeStatus.Available)),
            )
        )

    override suspend fun deleteCodePool(poolId: String): ApiResult<Unit> {
        if (writeResult is ApiResult.Ok) pools.removeAll { it.id == poolId }
        return writeResult
    }

    override suspend fun addCodes(poolId: String, body: AddCodesBody): ApiResult<Unit> {
        addedCodes += poolId to body
        return writeResult
    }

    private fun setStatus(id: String, status: String) {
        val index: Int = store.indexOfFirst { it.id == id }
        if (index >= 0) store[index] = store[index].copy(status = status)
    }
}
