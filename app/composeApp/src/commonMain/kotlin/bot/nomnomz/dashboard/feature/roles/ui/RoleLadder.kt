// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.roles.ui

import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.roles_role_everyone
import nomnomzbot.composeapp.generated.resources.roles_role_subscriber
import nomnomzbot.composeapp.generated.resources.roles_role_vip
import nomnomzbot.composeapp.generated.resources.roles_role_artist
import nomnomzbot.composeapp.generated.resources.roles_role_moderator
import nomnomzbot.composeapp.generated.resources.roles_role_lead_moderator
import nomnomzbot.composeapp.generated.resources.roles_role_editor
import nomnomzbot.composeapp.generated.resources.roles_role_broadcaster
import org.jetbrains.compose.resources.StringResource

/**
 * One named rung of the unified authorization ladder (roles-permissions §0). [level] is the internal comparison
 * value the backend gates on; [label] is the only thing users ever see — the numbers stay internal.
 */
internal data class LadderRung(val level: Int, val label: StringResource)

/**
 * The unified ladder low→high (0/2/4/6/10/20/30/40): Plane-A community rungs then Plane-B management rungs,
 * aligned on their shared Moderator rung. The action-floor pickers (the channel matrix and the admin platform
 * defaults) offer these by name; a row label maps a level onto one. The single source for the ladder→name mapping.
 */
internal val LadderRungs: List<LadderRung> = listOf(
    LadderRung(0, Res.string.roles_role_everyone),
    LadderRung(2, Res.string.roles_role_subscriber),
    LadderRung(4, Res.string.roles_role_vip),
    LadderRung(6, Res.string.roles_role_artist),
    LadderRung(10, Res.string.roles_role_moderator),
    LadderRung(20, Res.string.roles_role_lead_moderator),
    LadderRung(30, Res.string.roles_role_editor),
    LadderRung(40, Res.string.roles_role_broadcaster),
)

/**
 * The localized NAME of the ladder rung a [level] satisfies — the highest rung whose threshold it meets. Enforces
 * the "users never see numeric permission levels" rule: every effective/override level renders as a role name.
 */
internal fun ladderRoleLabel(level: Int): StringResource =
    LadderRungs.lastOrNull { level >= it.level }?.label ?: Res.string.roles_role_everyone
