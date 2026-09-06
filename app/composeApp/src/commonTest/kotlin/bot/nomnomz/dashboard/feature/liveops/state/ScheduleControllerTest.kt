// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.liveops.state

import bot.nomnomz.dashboard.core.io.JournalFileIO
import bot.nomnomz.dashboard.core.io.PickedFile
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.Category
import bot.nomnomz.dashboard.core.network.ChannelSearchResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateScheduleSegmentBody
import bot.nomnomz.dashboard.core.network.LiveOpsApi
import bot.nomnomz.dashboard.core.network.LiveOpsSchedule
import bot.nomnomz.dashboard.core.network.LiveOpsScheduleSegment
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.StreamApi
import bot.nomnomz.dashboard.core.network.StreamInfoUpdate
import bot.nomnomz.dashboard.core.network.UpdateScheduleSegmentBody
import bot.nomnomz.dashboard.core.network.UpdateScheduleSettingsBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the stream-schedule page shows the channel's REAL schedule and manages it: load surfaces the segments,
// adding one posts the ISO/duration/timezone through and re-reads the schedule so the new segment appears (the
// consequence, not merely that a call happened), and delete removes it and reloads.
class ScheduleControllerTest {

    @Test
    fun load_surfaces_the_channels_schedule_segments() = runTest {
        val api =
            FakeScheduleLiveOpsApi(
                schedule =
                    ApiResult.Ok(
                        LiveOpsSchedule(
                            segments = listOf(LiveOpsScheduleSegment(id = "s1", title = "Monday stream"))
                        )
                    )
            )
        val controller = ScheduleController(FakeScheduleChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeScheduleStreamApi(), FakeScheduleFileIO())

        controller.load()

        val state: ScheduleState = controller.state.value
        assertTrue(state is ScheduleState.Ready)
        assertEquals(listOf("s1"), (state as ScheduleState.Ready).schedule.segments.map { it.id })
    }

    @Test
    fun add_segment_posts_the_fields_and_reloads_so_the_new_stream_appears() = runTest {
        // First load: empty. After the add, the fake returns a schedule WITH the segment — proving the reload.
        val api =
            FakeScheduleLiveOpsApi(
                schedule = ApiResult.Ok(LiveOpsSchedule()),
                scheduleAfterWrite =
                    ApiResult.Ok(
                        LiveOpsSchedule(segments = listOf(LiveOpsScheduleSegment(id = "s9", title = "Friday")))
                    ),
            )
        val controller = ScheduleController(FakeScheduleChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeScheduleStreamApi(), FakeScheduleFileIO())
        controller.load()

        controller.addSegment(
            startTime = "2026-07-20T19:00:00Z",
            timezone = "Europe/Amsterdam",
            duration = "240",
            isRecurring = true,
            title = "Friday",
            categoryId = null,
        )

        // The create body carried the fields, and the reloaded page now shows the new segment.
        val created: CreateScheduleSegmentBody = api.created.single()
        assertEquals("2026-07-20T19:00:00Z", created.startTime)
        assertEquals("240", created.duration)
        assertEquals("Europe/Amsterdam", created.timezone)
        assertEquals(true, created.isRecurring)
        assertEquals(
            listOf("s9"),
            (controller.state.value as? ScheduleState.Ready)?.schedule?.segments?.map { it.id },
        )
    }

    @Test
    fun edit_segment_can_change_only_the_title_leaving_timezone_and_duration_unchanged() = runTest {
        // The dialog can't prefill timezone/duration (not on the segment read), so a title-only edit sends
        // them blank. editSegment must forward blanks as null so the backend leaves those fields unchanged
        // instead of wiping them — this is the write the relaxed canSave (edit needs only the start) reaches.
        val api =
            FakeScheduleLiveOpsApi(
                schedule =
                    ApiResult.Ok(
                        LiveOpsSchedule(segments = listOf(LiveOpsScheduleSegment(id = "s1", title = "Old")))
                    ),
                scheduleAfterWrite =
                    ApiResult.Ok(
                        LiveOpsSchedule(segments = listOf(LiveOpsScheduleSegment(id = "s1", title = "New")))
                    ),
            )
        val controller = ScheduleController(FakeScheduleChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeScheduleStreamApi(), FakeScheduleFileIO())
        controller.load()

        controller.editSegment(
            segmentId = "s1",
            startTime = "2026-07-20T19:00:00Z",
            duration = "",
            timezone = "",
            title = "New",
            categoryId = null,
        )

        val (id, body) = api.updated.single()
        assertEquals("s1", id)
        assertEquals("New", body.title)
        // Blank timezone/duration → null → the backend keeps them (the segment is not reset to defaults).
        assertNull(body.timezone)
        assertNull(body.duration)
        assertEquals("2026-07-20T19:00:00Z", body.startTime)
        assertEquals(
            listOf("New"),
            (controller.state.value as? ScheduleState.Ready)?.schedule?.segments?.map { it.title },
        )
    }

    @Test
    fun load_treats_a_channel_with_no_twitch_link_as_empty_not_an_error() = runTest {
        // The channel itself resolves fine (primaryChannel succeeds) — only the Twitch-only schedule read fails,
        // with the exact code+message the server's identity resolver returns when this channel has no
        // TwitchChannelId on record. That is a real EMPTY (no Twitch schedule to show), not a load failure.
        val api =
            FakeScheduleLiveOpsApi(
                schedule = ApiResult.Failure(ApiError(404, "not_found", "Channel is not known locally."))
            )
        val controller = ScheduleController(FakeScheduleChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeScheduleStreamApi(), FakeScheduleFileIO())

        controller.load()

        assertTrue(controller.state.value is ScheduleState.Empty)
    }

    @Test
    fun load_surfaces_a_genuine_schedule_failure_as_an_error_with_its_reason() = runTest {
        // A different failure on the SAME "not_found" code (Twitch itself reporting nothing, not our own
        // identity-resolution short-circuit) must NOT be swallowed into Empty — it is a real failure and the
        // page must show it, with its actual reason, so a genuine outage can never read as "nothing scheduled".
        val api =
            FakeScheduleLiveOpsApi(
                schedule = ApiResult.Failure(ApiError(404, "not_found", "Twitch returned no data."))
            )
        val controller = ScheduleController(FakeScheduleChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeScheduleStreamApi(), FakeScheduleFileIO())

        controller.load()

        val state: ScheduleState = controller.state.value
        assertTrue(state is ScheduleState.Error)
        assertEquals("Twitch returned no data.", (state as ScheduleState.Error).detail)
    }

    @Test
    fun load_surfaces_a_transport_failure_as_an_error_even_with_a_different_code() = runTest {
        // A wholly different failure shape (5xx / transport) also stays an error — proves the Empty mapping is
        // narrow to the one known-benign case, not a catch-all for every failure the schedule read can produce.
        val api =
            FakeScheduleLiveOpsApi(
                schedule = ApiResult.Failure(ApiError(503, "transport", "Twitch is unreachable."))
            )
        val controller = ScheduleController(FakeScheduleChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeScheduleStreamApi(), FakeScheduleFileIO())

        controller.load()

        val state: ScheduleState = controller.state.value
        assertTrue(state is ScheduleState.Error)
        assertEquals("Twitch is unreachable.", (state as ScheduleState.Error).detail)
    }

    @Test
    fun load_surfaces_an_unknown_channel_as_an_error_before_the_schedule_is_even_read() = runTest {
        // A genuinely unknown channel fails at channel resolution (primaryChannel), never reaching the schedule
        // read at all — this is the real ERROR case the Empty mapping must never mask.
        val channelsApi =
            FakeScheduleChannelsApi(ApiResult.Failure(ApiError(404, "not_found", "Channel is not known locally.")))
        val api = FakeScheduleLiveOpsApi(schedule = ApiResult.Ok(LiveOpsSchedule()))
        val controller = ScheduleController(channelsApi, api, FakeScheduleStreamApi(), FakeScheduleFileIO())

        controller.load()

        val state: ScheduleState = controller.state.value
        assertTrue(state is ScheduleState.Error)
        assertEquals("Channel is not known locally.", (state as ScheduleState.Error).detail)
    }

    @Test
    fun delete_segment_calls_the_api_and_reloads() = runTest {
        val api =
            FakeScheduleLiveOpsApi(
                schedule =
                    ApiResult.Ok(
                        LiveOpsSchedule(segments = listOf(LiveOpsScheduleSegment(id = "s1", title = "x")))
                    ),
                scheduleAfterWrite = ApiResult.Ok(LiveOpsSchedule()),
            )
        val controller = ScheduleController(FakeScheduleChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeScheduleStreamApi(), FakeScheduleFileIO())
        controller.load()

        controller.deleteSegment("s1")

        assertEquals(listOf("s1"), api.deleted)
        // Reloaded to the now-empty schedule → the page reports Empty.
        assertTrue(controller.state.value is ScheduleState.Empty)
    }

    @Test
    fun download_icalendar_saves_the_feed_bytes_as_an_ics_file() = runTest {
        val api =
            FakeScheduleLiveOpsApi(
                schedule =
                    ApiResult.Ok(
                        LiveOpsSchedule(segments = listOf(LiveOpsScheduleSegment(id = "s1", title = "x")))
                    ),
                icalendar = ApiResult.Ok("BEGIN:VCALENDAR\nX-WR-CALNAME:Stream\nEND:VCALENDAR"),
            )
        val files = FakeScheduleFileIO()
        val controller = ScheduleController(FakeScheduleChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeScheduleStreamApi(), files)
        controller.load()

        val saved: Boolean = controller.downloadIcalendar()

        assertTrue(saved)
        assertEquals("schedule.ics", files.savedName)
        assertEquals("BEGIN:VCALENDAR\nX-WR-CALNAME:Stream\nEND:VCALENDAR", files.savedBytes?.decodeToString())
    }

    @Test
    fun download_icalendar_surfaces_the_error_and_does_not_save_on_failure() = runTest {
        val api =
            FakeScheduleLiveOpsApi(
                schedule =
                    ApiResult.Ok(
                        LiveOpsSchedule(segments = listOf(LiveOpsScheduleSegment(id = "s1", title = "x")))
                    ),
                icalendar = ApiResult.Failure(ApiError(500, "SERVER_ERROR", "calendar unavailable")),
            )
        val files = FakeScheduleFileIO()
        val controller = ScheduleController(FakeScheduleChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api, FakeScheduleStreamApi(), files)
        controller.load()

        val saved: Boolean = controller.downloadIcalendar()

        assertTrue(!saved)
        assertEquals(null, files.savedName)
        assertEquals("calendar unavailable", (controller.state.value as ScheduleState.Ready).actionError)
    }
}

// Records the last save so a test can assert the exact filename + bytes handed to the platform file bridge.
private class FakeScheduleFileIO : JournalFileIO {
    var savedName: String? = null
    var savedBytes: ByteArray? = null

    override suspend fun saveFile(suggestedName: String, bytes: ByteArray): Boolean {
        savedName = suggestedName
        savedBytes = bytes
        return true
    }

    override suspend fun pickFile(): PickedFile? = null
}

// Only the category search matters to the schedule controller — it returns no matches here (the picker rows are
// exercised in UI, not this state test). info/update are not part of the schedule flow, so they throw if called.
private class FakeScheduleStreamApi : StreamApi {
    override suspend fun info(channelId: String) = error("unused")
    override suspend fun update(channelId: String, update: StreamInfoUpdate) = error("unused")
    override suspend fun searchCategories(channelId: String, query: String): ApiResult<List<Category>> =
        ApiResult.Ok(emptyList())

    override suspend fun searchChannels(channelId: String, query: String): ApiResult<List<ChannelSearchResult>> =
        ApiResult.Ok(emptyList())
}

private class FakeScheduleChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

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

// Only the schedule surface matters here: getSchedule returns [schedule] on the first read and [scheduleAfterWrite]
// (when set) on subsequent reads, so a test can prove the post-write reload observed the new state. Every non-
// schedule action throws if called (they are not part of this controller).
private class FakeScheduleLiveOpsApi(
    private val schedule: ApiResult<LiveOpsSchedule>,
    private val scheduleAfterWrite: ApiResult<LiveOpsSchedule>? = null,
    private val icalendar: ApiResult<String> = ApiResult.Ok("BEGIN:VCALENDAR\nEND:VCALENDAR"),
) : LiveOpsApi {
    val created: MutableList<CreateScheduleSegmentBody> = mutableListOf()
    val deleted: MutableList<String> = mutableListOf()
    val updated: MutableList<Pair<String, UpdateScheduleSegmentBody>> = mutableListOf()
    private var reads: Int = 0

    override suspend fun getSchedule(channelId: String): ApiResult<LiveOpsSchedule> {
        val result: ApiResult<LiveOpsSchedule> =
            if (reads > 0 && scheduleAfterWrite != null) scheduleAfterWrite else schedule
        reads++
        return result
    }

    override suspend fun getScheduleIcalendar(channelId: String): ApiResult<String> = icalendar

    override suspend fun createScheduleSegment(
        channelId: String,
        body: CreateScheduleSegmentBody,
    ): ApiResult<Unit> {
        created.add(body)
        return ApiResult.Ok(Unit)
    }

    override suspend fun updateScheduleSegment(
        channelId: String,
        segmentId: String,
        body: UpdateScheduleSegmentBody,
    ): ApiResult<Unit> {
        updated.add(segmentId to body)
        return ApiResult.Ok(Unit)
    }

    override suspend fun deleteScheduleSegment(channelId: String, segmentId: String): ApiResult<Unit> {
        deleted.add(segmentId)
        return ApiResult.Ok(Unit)
    }

    override suspend fun updateScheduleSettings(
        channelId: String,
        body: UpdateScheduleSettingsBody,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun getPolls(channelId: String) = error("unused")
    override suspend fun createPoll(channelId: String, body: bot.nomnomz.dashboard.core.network.CreatePollBody) =
        error("unused")
    override suspend fun endPoll(channelId: String, pollId: String, status: String) = error("unused")
    override suspend fun getPredictions(channelId: String) = error("unused")
    override suspend fun createPrediction(
        channelId: String,
        body: bot.nomnomz.dashboard.core.network.CreatePredictionBody,
    ) = error("unused")
    override suspend fun endPrediction(
        channelId: String,
        predictionId: String,
        status: String,
        winningOutcomeId: String?,
    ) = error("unused")
    override suspend fun startRaid(channelId: String, targetTwitchBroadcasterId: String) = error("unused")
    override suspend fun cancelRaid(channelId: String) = error("unused")
    override suspend fun getAdSchedule(channelId: String) = error("unused")
    override suspend fun startCommercial(channelId: String, lengthSeconds: Int) = error("unused")
    override suspend fun snoozeNextAd(channelId: String) = error("unused")
    override suspend fun createClip(channelId: String) = error("unused")
    override suspend fun createMarker(channelId: String, description: String?) = error("unused")
}
