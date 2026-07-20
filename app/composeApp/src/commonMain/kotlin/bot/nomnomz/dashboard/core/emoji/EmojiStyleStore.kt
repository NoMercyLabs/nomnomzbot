// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.emoji

// The common contract the EmojiStyleController depends on for persisting the chosen emoji rendering style —
// the per-target [EmojiStylePreferenceStore] implements it. Depending on the interface (not the expect
// class) keeps the controller testable with a fake, mirroring how the LanguageController depends on
// LanguageStore rather than the LanguagePreferenceStore expect.
//
// The persisted value is a style token: `null` = the default (color/Twemoji), `"monochrome"` = the
// monochrome (Noto Emoji) fallback. Both calls are defensive — a read/write failure falls back to the
// default (a `null` read) and never throws, so a corrupt store can never crash the dashboard.
interface EmojiStyleStore {
    fun read(): String?

    fun write(token: String?)
}
