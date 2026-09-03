// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem

import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.perm_artist
import nomnomzbot.composeapp.generated.resources.perm_broadcaster
import nomnomzbot.composeapp.generated.resources.perm_editor
import nomnomzbot.composeapp.generated.resources.perm_everyone
import nomnomzbot.composeapp.generated.resources.perm_lead_moderator
import nomnomzbot.composeapp.generated.resources.perm_moderator
import nomnomzbot.composeapp.generated.resources.perm_subscriber
import nomnomzbot.composeapp.generated.resources.perm_vip
import org.jetbrains.compose.resources.StringResource

/**
 * The permission ladder as the API now speaks it: rung NAMES, in ladder order, each with the label a person
 * reads.
 *
 * <p>Two screens used to keep their own `List<Pair<Int, StringResource>>` of raw ladder values, and both
 * listed five rungs where the ladder has eight — Artist, LeadModerator and Editor were unreachable from any
 * picker, so a command could be stored at one of them and then silently rewritten to a neighbour the moment
 * a streamer touched the form. One table, keyed by the name the wire carries, removes both problems.</p>
 */
object PermissionRungs {
    const val Everyone: String = "Everyone"

    /** Ladder order, lowest first — the order a picker offers them in. */
    val Ordered: List<Pair<String, StringResource>> =
        listOf(
            Everyone to Res.string.perm_everyone,
            "Subscriber" to Res.string.perm_subscriber,
            "Vip" to Res.string.perm_vip,
            "Artist" to Res.string.perm_artist,
            "Moderator" to Res.string.perm_moderator,
            "LeadModerator" to Res.string.perm_lead_moderator,
            "Editor" to Res.string.perm_editor,
            "Broadcaster" to Res.string.perm_broadcaster,
        )

    /**
     * The label for a rung name. An unknown name falls back to Everyone's label rather than rendering the raw
     * value — but only for DISPLAY: the stored value is never rewritten by a failed lookup.
     */
    fun labelOf(rung: String): StringResource =
        Ordered.firstOrNull { it.first.equals(rung, ignoreCase = true) }?.second
            ?: Res.string.perm_everyone
}
