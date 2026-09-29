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

/** An in-memory [ActAsSessionStore] standing in for the tab's sessionStorage, readable by the test. */
class InMemoryActAsSessionStore(var session: PersistedActAs? = null) : ActAsSessionStore {
    var notice: ActAsEndNotice? = null

    override fun read(): PersistedActAs? = session

    override fun write(session: PersistedActAs) {
        this.session = session
    }

    override fun clear() {
        session = null
    }

    override fun putNotice(notice: ActAsEndNotice) {
        this.notice = notice
    }

    override fun takeNotice(): ActAsEndNotice? {
        val taken: ActAsEndNotice? = notice
        notice = null
        return taken
    }
}
