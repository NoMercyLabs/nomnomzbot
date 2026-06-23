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

// The common contract the LanguageController depends on for persisting the chosen display language —
// the per-target [LanguagePreferenceStore] implements it. Depending on the interface (not the expect
// class) keeps the controller testable with a fake, mirroring how SessionStore depends on
// SessionTokenStore rather than the TokenVault expect.
//
// The persisted value is the BCP-47-ish language tag: `null` = follow the System/OS locale, `"en"` /
// `"nl"` = a forced language. Both calls are defensive — a read/write failure falls back to System
// default (a `null` read) and never throws, so a corrupt store can never crash the dashboard.
interface LanguageStore {
    fun read(): String?

    fun write(tag: String?)
}
