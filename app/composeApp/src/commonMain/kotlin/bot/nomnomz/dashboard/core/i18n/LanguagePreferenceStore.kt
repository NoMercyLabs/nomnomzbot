// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.i18n

// Per-target persistence of the chosen display language, surviving restart (mirrors TokenVault's
// per-target custody seam):
//
//   Desktop: a small text file under the OS app-data dir (same base dir as the token vault).
//   Web:     localStorage (persists across tab close, unlike the session-scoped token store) — the
//            language preference is per-install UI state, not a secret, so it lives beyond the session.
//
// A read/write failure falls back to System default and never throws (see [LanguageStore]).
expect class LanguagePreferenceStore() : LanguageStore {
    override fun read(): String?

    override fun write(tag: String?)
}
