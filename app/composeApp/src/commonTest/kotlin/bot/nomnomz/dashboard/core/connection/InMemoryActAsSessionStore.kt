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

/**
 * An in-memory [ActAsSessionStore] standing in for the tab's sessionStorage. [written] records every value that
 * was ever written to it, so a test can prove no token reached JS storage.
 */
class InMemoryActAsSessionStore(var returnLocation: String? = null) : ActAsSessionStore {
    var notice: ActAsEndNotice? = null
    val written: MutableList<String> = mutableListOf()

    override fun readReturnLocation(): String? = returnLocation

    override fun writeReturnLocation(location: String) {
        written += location
        returnLocation = location
    }

    override fun clear() {
        returnLocation = null
    }

    override fun putNotice(notice: ActAsEndNotice) {
        written += notice.toString()
        this.notice = notice
    }

    override fun takeNotice(): ActAsEndNotice? {
        val taken: ActAsEndNotice? = notice
        notice = null
        return taken
    }
}
