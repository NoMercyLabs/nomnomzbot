// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.editor

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

// The web project-editor's fire bar (ProjectEditor.wasmJs.kt `refreshFireBar`) used to post `{}` for every event it
// fired into the sandboxed preview, regardless of event type — a widget reading `message`/`fragments`/`amount`/etc.
// rendered nothing no matter which button was pressed, and every event looked identical in the preview. This is a
// Kotlin-side port of the server's `WidgetTestSamples` (WidgetTestEventController.cs) — the SAME representative
// payload shapes the "test this overlay" dashboard action fires for a live channel — so the in-browser preview shows
// a real, per-event-type sample instead of an empty object. `allSamplesJson()` is embedded once into the editor's
// JS blob at open time and indexed by event type client-side; unknown event types fall back to [DefaultSample].
internal object WidgetFireBarSamples {
    private val json: Json = Json { encodeDefaults = true }

    /** Every event type a shipped first-party widget can subscribe to, mapped to its representative sample. */
    fun sampleFor(eventType: String): JsonObject =
        when (eventType) {
            "follow" -> buildJsonObject { put("user", "TestFollower") }
            "subscription" ->
                buildJsonObject {
                    put("user", "TestSubscriber")
                    put("tier", "1000")
                }
            "resub" ->
                buildJsonObject {
                    put("user", "TestResubber")
                    put("months", 6)
                    put("tier", "1000")
                }
            "gift" ->
                buildJsonObject {
                    put("user", "TestGifter")
                    put("amount", 5)
                    put("tier", "1000")
                }
            "cheer" ->
                buildJsonObject {
                    put("user", "TestCheerer")
                    put("amount", 500)
                }
            "raid" ->
                buildJsonObject {
                    put("user", "TestRaider")
                    put("viewers", 42)
                }
            "ban" -> buildJsonObject { put("user", "TestUser") }
            "supporter.tip" ->
                buildJsonObject {
                    put("user", "TestTipper")
                    put("amount", 25)
                    put("currency", "USD")
                }
            "supporter.membership" -> buildJsonObject { put("user", "TestMember") }
            "supporter.merch" -> buildJsonObject { put("user", "TestBuyer") }
            "supporter.charity" ->
                buildJsonObject {
                    put("user", "TestDonor")
                    put("amount", 50)
                    put("currency", "USD")
                }
            "now_playing" ->
                buildJsonObject {
                    put("isPlaying", true)
                    put("track", "Test Track")
                    put("artist", "Test Artist")
                }
            "hype_train_begin", "hype_train_progress" ->
                buildJsonObject {
                    put("level", 2)
                    put("progress", 350)
                    put("goal", 1000)
                }
            "hype_train_end" -> buildJsonObject { put("level", 3) }
            "goal" ->
                buildJsonObject {
                    put("metric", "followers")
                    put("value", 72)
                    put("target", 100)
                }
            "sr_queue" ->
                buildJsonObject {
                    put(
                        "items",
                        buildJsonArray {
                            add(songRequest("Never Gonna Give You Up", "TestViewer", 213))
                            add(songRequest("Sandstorm", "AnotherViewer", 225))
                            add(songRequest("Bohemian Rhapsody", "ThirdViewer", 355))
                        },
                    )
                }
            "tts_speak" ->
                buildJsonObject {
                    put("text", "Hey streamer, thanks for the awesome content!")
                    put("voice", "en-US-JennyNeural")
                    put("user", "TestViewer")
                    put("durationMs", 3200)
                }
            "poll_begin", "poll_progress" -> pollFrame(winningChoiceId = null)
            "poll_end" -> pollFrame(winningChoiceId = "c1")
            "prediction_begin", "prediction_progress", "prediction_lock" -> predictionFrame(winningOutcomeId = null)
            "prediction_end" -> predictionFrame(winningOutcomeId = "o1")
            "reward_redeemed" ->
                buildJsonObject {
                    put("rewardId", "test-reward")
                    put("rewardTitle", "Hydrate!")
                    put("userDisplayName", "TestRedeemer")
                    put("cost", 500)
                    put("userInput", "Drink some water please!")
                }
            "custom.heartrate" -> buildJsonObject { put("fields", buildJsonObject { put("bpm", 142) }) }
            "game.lobby", "game.running" ->
                buildJsonObject {
                    put("kind", "round_open")
                    put("target", 50)
                    put("radius", 12)
                }
            "game.resolved" ->
                buildJsonObject {
                    put("kind", "results")
                    put("target", 50)
                    put("winner", "TestWinner")
                    put(
                        "results",
                        buildJsonArray {
                            add(
                                buildJsonObject {
                                    put("player", "TestWinner")
                                    put("won", true)
                                    put("payout", 500)
                                    put("landed", 50)
                                    put("distance", 0)
                                    put("multiplier", 2.5)
                                }
                            )
                            add(
                                buildJsonObject {
                                    put("player", "SecondPlace")
                                    put("won", false)
                                    put("payout", 0)
                                    put("landed", 34)
                                    put("distance", 16)
                                    put("multiplier", 0.0)
                                }
                            )
                        },
                    )
                }
            "ChatMessage" -> chatMessage()
            else -> DefaultSample
        }

    /** All event types this catalogue covers, keyed for the fire bar's JS lookup, JSON-encoded once per editor open. */
    fun allSamplesJson(): String =
        json.encodeToString(
            JsonObject.serializer(),
            buildJsonObject {
                for (eventType in KnownEventTypes) put(eventType, sampleFor(eventType))
                put(DefaultKey, DefaultSample)
            },
        )

    /** The fallback the JS fire bar uses for an event type outside [KnownEventTypes] (mirrors the server's `_`). */
    const val DefaultKey: String = "_default"

    private val DefaultSample: JsonObject = buildJsonObject { put("user", "TestUser") }

    private val KnownEventTypes: List<String> =
        listOf(
            "follow",
            "subscription",
            "resub",
            "gift",
            "cheer",
            "raid",
            "ban",
            "supporter.tip",
            "supporter.membership",
            "supporter.merch",
            "supporter.charity",
            "now_playing",
            "hype_train_begin",
            "hype_train_progress",
            "hype_train_end",
            "goal",
            "sr_queue",
            "tts_speak",
            "poll_begin",
            "poll_progress",
            "poll_end",
            "prediction_begin",
            "prediction_progress",
            "prediction_lock",
            "prediction_end",
            "reward_redeemed",
            "custom.heartrate",
            "game.lobby",
            "game.running",
            "game.resolved",
            "ChatMessage",
        )

    private fun songRequest(title: String, requestedBy: String, durationSec: Int): JsonObject =
        buildJsonObject {
            put("title", title)
            put("requestedBy", requestedBy)
            put("durationSec", durationSec)
        }

    private fun pollFrame(winningChoiceId: String?): JsonObject =
        buildJsonObject {
            put("pollId", "test-poll")
            put("title", "What game next?")
            winningChoiceId?.let { put("winningChoiceId", it) }
            val votes: Triple<Int, Int, Int> = if (winningChoiceId == null) Triple(42, 28, 15) else Triple(62, 28, 15)
            val cpVotes: Triple<Int, Int, Int> = if (winningChoiceId == null) Triple(10, 5, 0) else Triple(18, 5, 0)
            put(
                "choices",
                buildJsonArray {
                    add(pollChoice("c1", "Elden Ring", votes.first, cpVotes.first))
                    add(pollChoice("c2", "Minecraft", votes.second, cpVotes.second))
                    add(pollChoice("c3", "Just Chatting", votes.third, cpVotes.third))
                },
            )
        }

    private fun pollChoice(id: String, title: String, votes: Int, channelPointsVotes: Int): JsonObject =
        buildJsonObject {
            put("id", id)
            put("title", title)
            put("votes", votes)
            put("channelPointsVotes", channelPointsVotes)
        }

    private fun predictionFrame(winningOutcomeId: String?): JsonObject =
        buildJsonObject {
            put("predictionId", "test-pred")
            put("title", "Will we beat the boss this attempt?")
            winningOutcomeId?.let { put("winningOutcomeId", it) }
            put(
                "outcomes",
                buildJsonArray {
                    add(predictionOutcome("o1", "Yes", 12000, 42, "BLUE"))
                    add(predictionOutcome("o2", "No", 4500, 18, "PINK"))
                },
            )
        }

    private fun predictionOutcome(id: String, title: String, channelPoints: Int, users: Int, color: String): JsonObject =
        buildJsonObject {
            put("id", id)
            put("title", title)
            put("channelPoints", channelPoints)
            put("users", users)
            put("color", color)
        }

    private fun chatMessage(): JsonObject =
        buildJsonObject {
            put("userId", "test-chatter")
            put("username", "testchatter")
            put("displayName", "TestChatter")
            put("color", "#9146ff")
            put("message", "Hey chat! Kappa this stream is amazing LUL 4Head")
            put(
                "fragments",
                buildJsonArray {
                    add(textFragment("Hey chat! "))
                    add(emoteFragment("Kappa", "25"))
                    add(textFragment(" this stream is amazing "))
                    add(emoteFragment("LUL", "425618"))
                    add(textFragment(" "))
                    add(emoteFragment("4Head", "354"))
                },
            )
            put(
                "badges",
                buildJsonArray {
                    add(
                        buildJsonObject {
                            put("setId", "broadcaster")
                            put(
                                "urls",
                                buildJsonObject {
                                    put("1", "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85cc1/1")
                                    put("2", "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85cc1/2")
                                    put("4", "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85cc1/3")
                                },
                            )
                        }
                    )
                },
            )
            put("pronouns", "they/them")
        }

    private fun textFragment(text: String): JsonObject =
        buildJsonObject {
            put("type", "text")
            put("text", text)
        }

    private fun emoteFragment(name: String, id: String): JsonObject =
        buildJsonObject {
            put("type", "emote")
            put("text", name)
            put(
                "emote",
                buildJsonObject {
                    put("id", id)
                    put("provider", "twitch")
                    put(
                        "urls",
                        buildJsonObject {
                            put("1", "https://static-cdn.jtvnw.net/emoticons/v2/" + id + "/default/dark/1.0")
                            put("2", "https://static-cdn.jtvnw.net/emoticons/v2/" + id + "/default/dark/2.0")
                            put("3", "https://static-cdn.jtvnw.net/emoticons/v2/" + id + "/default/dark/3.0")
                        },
                    )
                },
            )
        }
}
