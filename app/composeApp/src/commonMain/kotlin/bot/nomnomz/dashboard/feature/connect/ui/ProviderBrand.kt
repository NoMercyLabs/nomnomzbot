// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.connect.ui

import androidx.compose.runtime.Immutable
import androidx.compose.ui.graphics.Color

// ─────────────────────────────────────────────────────────────────────────────
//  THE BRAND-COLOR EXCEPTION (deliberate, documented, single-homed).
//
//  The design system (frontend-design-system.md §8) is tokens-only — components never
//  touch a raw hex / Color(0x…); the detekt linter bans them everywhere. Provider brand
//  identity is the ONE sanctioned exception: Twitch purple, Spotify green, Discord
//  blurple, and YouTube red are the providers' OWN trademarks, not values we may derive
//  from the neutral token palette or the user's dynamic accent. Recolouring them would
//  misrepresent the brand and break recognisability.
//
//  So every provider brand colour lives HERE and ONLY here — one small, audited object
//  the no-raw-hex rule allow-lists by file. Nothing else in the codebase carries a brand
//  hex inline; the connect modal and its descriptors read these named values. Everything
//  else (card, text, spacing, radius, typography) stays on LocalTokens/LocalSpacing/etc.
// ─────────────────────────────────────────────────────────────────────────────

/** One provider's official brand colours, used only to paint the connect modal's brand surfaces. */
@Immutable
data class ProviderBrandColors(
    /** The provider's primary brand colour — the CTA background and the ambient backdrop blobs. */
    val brand: Color,
    /** The text/icon colour that sits ON [brand] at WCAG-legible contrast (white or black per brand). */
    val onBrand: Color,
)

/**
 * The closed set of provider brand palettes (the documented brand-colour exception home).
 * Hex values are each provider's published brand colour:
 *   - Twitch  `#9146FF`  (Twitch purple, white foreground)
 *   - Spotify `#1DB954`  (Spotify green, BLACK foreground per brand contrast)
 *   - Discord `#5865F2`  (Discord blurple, white foreground)
 *   - YouTube `#FF0000`  (YouTube red, white foreground)
 *   - Kick    `#53FC18`  (Kick green, BLACK foreground — bright green needs a dark on-colour, like Spotify)
 *   - X       inverted   (see below — X's mark is monochrome black/white, so it can't carry a fixed hue)
 */
object ProviderBrand {
    /** Foreground that reads on a bright brand surface (Spotify green, Kick green) — black. */
    private val BlackOnBrand: Color = Color(0xFF000000)

    /** Foreground that reads on the darker purple/blurple/red brands — white. */
    private val LightOnBrand: Color = Color(0xFFFFFFFF)

    val Twitch: ProviderBrandColors =
        ProviderBrandColors(brand = Color(0xFF9146FF), onBrand = LightOnBrand)

    val Spotify: ProviderBrandColors =
        ProviderBrandColors(brand = Color(0xFF1DB954), onBrand = BlackOnBrand)

    val Discord: ProviderBrandColors =
        ProviderBrandColors(brand = Color(0xFF5865F2), onBrand = LightOnBrand)

    val YouTube: ProviderBrandColors =
        ProviderBrandColors(brand = Color(0xFFFF0000), onBrand = LightOnBrand)

    val Kick: ProviderBrandColors =
        ProviderBrandColors(brand = Color(0xFF53FC18), onBrand = BlackOnBrand)

    // X (Twitter): the mark is monochrome — black-on-white or white-on-black — so it has no coloured hue to
    // paint the CTA with. Pure `#000000` as the CTA surface is invisible against the near-black connect
    // backdrop (and fails WCAG contrast in dark mode), so X uses the INVERTED chip treatment: a WHITE surface
    // carrying the BLACK X mark, which reads cleanly on the dark card. (A future light theme would want the
    // opposite inversion; the correct long-term fix is a foreground-token-driven surface, but ProviderBrand
    // holds raw brand values only — the app currently renders in the dark scheme, where white-on-dark is right.)
    val X: ProviderBrandColors =
        ProviderBrandColors(brand = Color(0xFFFFFFFF), onBrand = BlackOnBrand)
}
