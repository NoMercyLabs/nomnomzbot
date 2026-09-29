// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.connection

// Desktop act-as custody — process memory. It outlives the in-process app restart that act-as begin/exit runs
// (a fresh AppGraph reads it back), but never touches disk: a relaunch always starts as the operator.
actual class ActAsSessionVault : ActAsSessionStore {

    actual override fun read(): PersistedActAs? = ProcessSlot.session

    actual override fun write(session: PersistedActAs) {
        ProcessSlot.session = session
    }

    actual override fun clear() {
        ProcessSlot.session = null
    }

    actual override fun putNotice(notice: ActAsEndNotice) {
        ProcessSlot.notice = notice
    }

    actual override fun takeNotice(): ActAsEndNotice? {
        val notice: ActAsEndNotice? = ProcessSlot.notice
        ProcessSlot.notice = null
        return notice
    }

    private object ProcessSlot {
        @Volatile var session: PersistedActAs? = null

        @Volatile var notice: ActAsEndNotice? = null
    }
}
