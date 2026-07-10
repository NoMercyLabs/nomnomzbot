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
import androidx.compose.ui.graphics.vector.ImageVector
import org.jetbrains.compose.resources.StringResource
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.connect_cta_discord
import nomnomzbot.composeapp.generated.resources.connect_cta_kick
import nomnomzbot.composeapp.generated.resources.connect_cta_spotify
import nomnomzbot.composeapp.generated.resources.connect_cta_twitch
import nomnomzbot.composeapp.generated.resources.connect_cta_twitter
import nomnomzbot.composeapp.generated.resources.connect_cta_youtube
import nomnomzbot.composeapp.generated.resources.connect_logo_cd_discord
import nomnomzbot.composeapp.generated.resources.connect_logo_cd_spotify
import nomnomzbot.composeapp.generated.resources.connect_logo_cd_twitch
import nomnomzbot.composeapp.generated.resources.connect_logo_cd_youtube
import nomnomzbot.composeapp.generated.resources.connect_modal_heading_discord
import nomnomzbot.composeapp.generated.resources.connect_modal_heading_spotify
import nomnomzbot.composeapp.generated.resources.connect_modal_heading_twitch
import nomnomzbot.composeapp.generated.resources.connect_modal_heading_youtube
import nomnomzbot.composeapp.generated.resources.connect_modal_subtitle_discord
import nomnomzbot.composeapp.generated.resources.connect_modal_subtitle_spotify
import nomnomzbot.composeapp.generated.resources.connect_modal_subtitle_twitch
import nomnomzbot.composeapp.generated.resources.connect_modal_subtitle_youtube

// The per-provider descriptor that drives [ConnectModal]. Each provider (Twitch login + the three
// integration connects) is described once, declaratively — brand palette, logo glyph, and the copy
// resources — so the modal itself stays a single generic, branded shell with no per-provider branches.
// Brand colours come from the documented [ProviderBrand] exception home; everything else is on-token.

/** One provider's full presentation contract for the connect modal. */
@Immutable
data class ConnectProvider(
    /** The provider's official brand colours (CTA background + ambient backdrop + on-brand text). */
    val brand: ProviderBrandColors,
    /** The provider's logo, drawn at the right of the dual-logo header and inside the CTA. */
    val providerLogo: ImageVector,
    /** Accessibility label for [providerLogo]. */
    val providerLogoDescription: StringResource,
    /** The bold white modal heading (e.g. "Welcome to NomNomzBot" / "Link your Spotify account"). */
    val heading: StringResource,
    /** The muted subtitle under the heading describing what connecting unlocks. */
    val subtitle: StringResource,
    /** The primary CTA label (e.g. "Connect with twitch"). */
    val ctaLabel: StringResource,
)

/** The closed set of connect-modal provider descriptors. */
object ConnectProviders {
    val Twitch: ConnectProvider =
        ConnectProvider(
            brand = ProviderBrand.Twitch,
            providerLogo = TwitchLogoGlyph,
            providerLogoDescription = Res.string.connect_logo_cd_twitch,
            heading = Res.string.connect_modal_heading_twitch,
            subtitle = Res.string.connect_modal_subtitle_twitch,
            ctaLabel = Res.string.connect_cta_twitch,
        )

    val Spotify: ConnectProvider =
        ConnectProvider(
            brand = ProviderBrand.Spotify,
            providerLogo = SpotifyLogoGlyph,
            providerLogoDescription = Res.string.connect_logo_cd_spotify,
            heading = Res.string.connect_modal_heading_spotify,
            subtitle = Res.string.connect_modal_subtitle_spotify,
            ctaLabel = Res.string.connect_cta_spotify,
        )

    val Discord: ConnectProvider =
        ConnectProvider(
            brand = ProviderBrand.Discord,
            providerLogo = DiscordLogoGlyph,
            providerLogoDescription = Res.string.connect_logo_cd_discord,
            heading = Res.string.connect_modal_heading_discord,
            subtitle = Res.string.connect_modal_subtitle_discord,
            ctaLabel = Res.string.connect_cta_discord,
        )

    val YouTube: ConnectProvider =
        ConnectProvider(
            brand = ProviderBrand.YouTube,
            providerLogo = YouTubeLogoGlyph,
            providerLogoDescription = Res.string.connect_logo_cd_youtube,
            heading = Res.string.connect_modal_heading_youtube,
            subtitle = Res.string.connect_modal_subtitle_youtube,
            ctaLabel = Res.string.connect_cta_youtube,
        )
}

// The minimal brand presentation for ONE endpoint-driven login button — just what a [ProviderBrandCta]
// paints: the brand palette, the provider mark, and the CTA label. Deliberately lighter than the full
// [ConnectProvider] descriptor (no heading/subtitle) because the login screen renders these providers as
// CTA buttons inside the Twitch-branded card, never as its header — so Kick/X need no card copy of their own.
@Immutable
data class LoginProviderCta(
    val brand: ProviderBrandColors,
    val logo: ImageVector,
    val label: StringResource,
)

/**
 * Resolve the brand CTA presentation for a backend login-provider key (from `GET /api/v1/auth/providers`),
 * or null for a key the client has no branding for (so the screen simply skips it rather than drawing an
 * unbranded button). Twitter is keyed `twitter` on the backend but presents as the X mark.
 */
fun loginProviderCta(key: String): LoginProviderCta? =
    when (key.lowercase()) {
        "twitch" -> LoginProviderCta(ProviderBrand.Twitch, TwitchLogoGlyph, Res.string.connect_cta_twitch)
        "youtube" -> LoginProviderCta(ProviderBrand.YouTube, YouTubeLogoGlyph, Res.string.connect_cta_youtube)
        "kick" -> LoginProviderCta(ProviderBrand.Kick, KickLogoGlyph, Res.string.connect_cta_kick)
        "twitter", "x" -> LoginProviderCta(ProviderBrand.X, XLogoGlyph, Res.string.connect_cta_twitter)
        else -> null
    }
