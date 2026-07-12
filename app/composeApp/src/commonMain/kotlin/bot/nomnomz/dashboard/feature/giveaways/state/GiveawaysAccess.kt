// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.giveaways.state

// The Giveaways page's capability gate. The campaign + lifecycle surface gates on `giveaways:write`, while the
// code pools AND the winner code reveal are held to the Broadcaster-only `giveaways:codes:write` — pools carry
// valuable secrets, so their reads and the one plaintext reveal path sit at the top of the ladder (giveaways.md
// §6, D6). The page reflects both floors through the caller's RESOLVED held action keys
// (`ResolvedAccess.heldActionKeys`, which folds in any per-action override), never a raw management role — so a
// Moderator who clears `giveaways:write` can run campaigns, while only a Broadcaster (holding
// `giveaways:codes:write`) sees the code-pool tools and can reveal a winner's code. These are pure predicates
// over the held-key set, independent of each other and testable without rendering Compose; the screen only turns
// the outcome into a disable-with-reason `ManageGate` (or hides the Broadcaster-only code section).
object GiveawaysAccess {
    /** The backend action key that gates reading the Giveaways page (GiveawaysController read actions). */
    const val ReadAction: String = "giveaways:read"

    /** The backend action key that gates the campaign + lifecycle writes (create/edit/delete/open/close/draw). */
    const val WriteAction: String = "giveaways:write"

    /** The Broadcaster-only key gating the code pools AND the winner code reveal (GiveawayCodePoolsController). */
    const val CodesAction: String = "giveaways:codes:write"

    /** Whether the caller may create/edit/delete a giveaway and run its lifecycle — they hold [WriteAction]. */
    fun canWrite(heldActionKeys: Set<String>): Boolean = WriteAction in heldActionKeys

    /**
     * Whether the caller may manage the secret-safe code pools AND reveal a winner's assigned code — both gate on
     * the same Broadcaster-only [CodesAction]. Drives whether the code-pool section is offered at all and whether
     * the winner-reveal control is live.
     */
    fun canManageCodes(heldActionKeys: Set<String>): Boolean = CodesAction in heldActionKeys
}
