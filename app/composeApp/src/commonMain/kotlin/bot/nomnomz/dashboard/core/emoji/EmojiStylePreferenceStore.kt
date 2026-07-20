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

// Per-target persistence of the chosen emoji rendering style, surviving restart (mirrors the display
// language's [LanguagePreferenceStore]):
//
//   Desktop: a small text file under the OS app-data dir (same base dir as the token vault + language file).
//   Web:     localStorage (persists across tab close, unlike the session-scoped token store) — the emoji
//            style is per-install UI state, not a secret, so it lives beyond the session.
//
// A read/write failure falls back to the default color style and never throws (see [EmojiStyleStore]).
expect class EmojiStylePreferenceStore() : EmojiStyleStore {
    override fun read(): String?

    override fun write(token: String?)
}
