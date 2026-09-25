// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.setup.state

import kotlinx.serialization.Serializable

// The non-secret onboarding basics [SetupController.finish] persists BEFORE handing off to the streamer
// OAuth, so they survive a web full-page redirect (which tears the in-memory SetupController state down
// before completeSetup()/applyBasics() can run). NEVER carries a credential, token, or client secret — only
// the review step's plain-text prefix/locale/timezone/bot-line-prefix and whether a dedicated platform bot
// was connected (decides whether [botLinePrefix] still applies, D5).
@Serializable
data class SetupFinishPending(
    val prefix: String,
    val locale: String,
    val timezone: String,
    val botLinePrefix: String,
    val platformBotConnected: Boolean,
)

// The common contract [SetupController] and the app-boot resume path depend on for persisting a pending
// setup-finish record — the per-target [SetupFinishPendingStore] implements it. Depending on the interface
// (not the expect class) keeps both callers testable with a fake, mirroring LanguageController/LanguageStore.
// Read/write are synchronous (file / localStorage I/O), like [bot.nomnomz.dashboard.core.i18n.LanguageStore].
interface SetupFinishStore {
    /** The pending record, or null when none is outstanding. */
    fun read(): SetupFinishPending?

    /** Persist [pending], or clear the record entirely when null. */
    fun write(pending: SetupFinishPending?)
}

// Per-target persistence of the pending setup-finish record, surviving a full-page reload (web) or an app
// restart (desktop) — mirrors [bot.nomnomz.dashboard.core.i18n.LanguagePreferenceStore]'s per-target custody
// seam:
//
//   Desktop: a small JSON file under the OS app-data dir (same base dir as the token vault).
//   Web:     localStorage — survives the full-page redirect the streamer OAuth performs.
//
// A read/write failure degrades to "no pending record" / a no-op write and never throws.
expect class SetupFinishPendingStore() : SetupFinishStore {
    override fun read(): SetupFinishPending?

    override fun write(pending: SetupFinishPending?)
}
