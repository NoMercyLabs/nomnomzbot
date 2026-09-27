// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.state

import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary

/**
 * The users the Promote dialog may offer: every listed user who is not already backing a principal. A
 * repeat promote is refused by the server (one principal per user), so the dialog never offers it.
 */
internal fun promoteCandidates(users: List<AdminUser>, principals: List<IamPrincipalSummary>): List<AdminUser> {
    val alreadyPrincipals: Set<String> = principals.mapNotNull { it.userId }.toSet()
    return users.filter { it.id !in alreadyPrincipals }
}
