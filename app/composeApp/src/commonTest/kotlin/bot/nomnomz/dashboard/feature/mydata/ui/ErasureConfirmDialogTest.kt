// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.mydata.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ErasurePreview
import bot.nomnomz.dashboard.core.network.ErasurePreviewCategory
import kotlin.test.Test

/**
 * S-CONSEQ-c1: proves the GDPR erasure confirm dialog RENDERS the backend's counted blast radius before the
 * most irreversible action in the product — the real per-category counts, the explicit "nothing would be
 * destroyed" sentence for a genuine zero, and a distinct failure message that never collapses into that zero.
 * Every render is pinned to `en`; the platform default locale is not guaranteed to be English.
 */
@OptIn(ExperimentalTestApi::class)
class ErasureConfirmDialogTest {

    private fun preview(vararg categories: Pair<String, Int>): ErasurePreview =
        ErasurePreview(
            subjectAlreadyAnonymized = false,
            totalRows = categories.sumOf { it.second },
            categories = categories.map { ErasurePreviewCategory(it.first, it.second) },
        )

    @Test
    fun a_subject_with_data_renders_the_real_counted_categories() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    ErasureConfirmDialog(
                        preview =
                            ErasurePreviewLoadState.Loaded(
                                preview(
                                    "gdpr_erasure_category_chat_messages" to 412,
                                    "gdpr_erasure_category_records" to 1,
                                    "gdpr_erasure_category_keys" to 2,
                                )
                            ),
                        onConfirm = {},
                        onDismiss = {},
                    )
                }
            }
        }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("412 chat messages", substring = true).fetchSemanticsNodes().isNotEmpty()
        }
        // Real counts, and the singular form where the count is genuinely one.
        onNodeWithText("412 chat messages", substring = true).assertExists()
        onNodeWithText("1 activity record", substring = true).assertExists()
        onNodeWithText("2 encryption keys", substring = true).assertExists()
        // A category the backend did not send is not invented as a zero line.
        onNodeWithText("active session", substring = true).assertDoesNotExist()
    }

    @Test
    fun a_subject_with_nothing_renders_the_explicit_nothing_sentence() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    ErasureConfirmDialog(
                        preview = ErasurePreviewLoadState.Loaded(ErasurePreview()),
                        onConfirm = {},
                        onDismiss = {},
                    )
                }
            }
        }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("Nothing would be destroyed", substring = true).fetchSemanticsNodes().isNotEmpty()
        }
        onNodeWithText("Nothing would be destroyed", substring = true).assertExists()
    }

    @Test
    fun a_failed_lookup_renders_its_own_distinct_message_never_zero() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    ErasureConfirmDialog(
                        preview = ErasurePreviewLoadState.Failed,
                        onConfirm = {},
                        onDismiss = {},
                    )
                }
            }
        }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("Could not check what would be erased", substring = true)
                .fetchSemanticsNodes()
                .isNotEmpty()
        }
        onNodeWithText("Could not check what would be erased", substring = true).assertExists()
        // The lie this guard exists to prevent: a failed lookup reading as a verified-safe zero.
        onNodeWithText("Nothing would be destroyed", substring = true).assertDoesNotExist()
    }

    @Test
    fun a_loading_lookup_renders_its_own_distinct_message() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    ErasureConfirmDialog(
                        preview = ErasurePreviewLoadState.Loading,
                        onConfirm = {},
                        onDismiss = {},
                    )
                }
            }
        }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("Checking exactly what would be erased", substring = true)
                .fetchSemanticsNodes()
                .isNotEmpty()
        }
        onNodeWithText("Checking exactly what would be erased", substring = true).assertExists()
        onNodeWithText("Nothing would be destroyed", substring = true).assertDoesNotExist()
    }

    @Test
    fun an_unknown_blast_radius_cannot_be_confirmed() = runComposeUiTest {
        var confirmed = false
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    ErasureConfirmDialog(
                        preview = ErasurePreviewLoadState.Failed,
                        onConfirm = { confirmed = true },
                        onDismiss = {},
                    )
                }
            }
        }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("Erase everything", substring = true).fetchSemanticsNodes().isNotEmpty()
        }
        // Erasure is irreversible and crypto-shreds keys: with the radius unknown, the affirmative must not
        // fire. Clicking a disabled destructive confirm must do nothing at all.
        onNodeWithText("Erase everything", substring = true).performClick()
        waitForIdle()
        kotlin.test.assertFalse(confirmed, "a destructive erasure must not be confirmable on an unknown blast radius")
    }
}
