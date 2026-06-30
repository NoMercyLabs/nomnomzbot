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

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.hoverable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.remember
import androidx.compose.ui.input.pointer.PointerIcon
import androidx.compose.ui.input.pointer.pointerHoverIcon
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.component.LinkedText
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.connect_logo_cd_app
import nomnomzbot.composeapp.generated.resources.connect_modal_back
import nomnomzbot.composeapp.generated.resources.connect_modal_logo_separator_cd
import nomnomzbot.composeapp.generated.resources.connect_modal_sign_in_prompt
import nomnomzbot.composeapp.generated.resources.connect_modal_terms

// The reusable, branded connect/login modal (recreated from the owner's reference design). A full-screen
// near-black backdrop carrying a soft ambient RADIAL glow in the provider's brand colour, a centred dark
// card, a dual-logo header (NomNomz mark tinted in the brand · provider logo), a bold heading + muted
// subtitle + a "please sign in" line, a full-width brand-coloured primary CTA, and OPTIONAL secondary Back
// button + footer. It is purely presentational: it owns no auth state and runs no flow — the host wires the
// CTA/Back callbacks to the real controller actions. Brand colours come from the documented [ProviderBrand]
// exception; every other surface (card, text, spacing, radius, typography) is on-token.

private val CardMaxWidth = 440.dp
private val CardCornerRadius = 16.dp
private val AppLogoSize = 44.dp
private val ProviderLogoSize = 32.dp
private val LogoSeparatorSize = 16.dp
private val CtaCornerRadius = 14.dp
private val CtaIconSize = 20.dp
private val BackIconSize = 18.dp

/**
 * Render the branded connect modal for [provider].
 *
 * @param heading overrides the descriptor's heading when a host needs a context-specific title (e.g. the
 *   first Twitch login uses "Welcome to NomNomzBot"); defaults to [ConnectProvider.heading].
 * @param onCta the primary action (run the OAuth / device login / provider connect) — null disables the CTA
 *   while a flow is mid-flight, so the host can keep the card visible with its own progress content.
 * @param onBack when non-null, renders the secondary "Back" button beneath the CTA (the per-provider
 *   connect flows); the first login passes null.
 * @param showTerms when true, renders the Terms/Privacy footer (first login only).
 * @param content optional extra content rendered inside the card BELOW the CTA block — e.g. the live
 *   device-code panel or a status line — so flow-specific UI lives in the same card.
 */
@Composable
fun ConnectModal(
    provider: ConnectProvider,
    onCta: (() -> Unit)?,
    modifier: Modifier = Modifier,
    heading: StringResource = provider.heading,
    onBack: (() -> Unit)? = null,
    showTerms: Boolean = false,
    content: @Composable () -> Unit = {},
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(
        modifier =
            modifier
                .fillMaxSize()
                .background(tokens.background)
                .ambientBrandGlow(provider.brand.brand)
                .windowInsetsPadding(WindowInsets.safeDrawing)
                .verticalScroll(rememberScrollState())
                .padding(spacing.s6),
        contentAlignment = Alignment.Center,
    ) {
        val cardShape = RoundedCornerShape(CardCornerRadius)
        Column(
            modifier =
                Modifier
                    .widthIn(max = CardMaxWidth)
                    .fillMaxWidth()
                    .clip(cardShape)
                    .background(tokens.card, cardShape)
                    .border(BorderStroke(spacing.s0_5 / 2, tokens.border), cardShape)
                    .padding(horizontal = spacing.s8, vertical = spacing.s8),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s4),
        ) {
            DualLogoHeader(provider = provider)

            Text(
                text = stringResource(heading),
                style = typography.xl2.copy(fontWeight = FontWeight.Bold),
                color = tokens.foreground,
                textAlign = TextAlign.Center,
            )
            Text(
                text = stringResource(provider.subtitle),
                style = typography.sm,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            Text(
                text = stringResource(Res.string.connect_modal_sign_in_prompt),
                style = typography.sm,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
                modifier = Modifier.padding(top = spacing.s2),
            )

            if (onCta != null) {
                BrandCta(provider = provider, onClick = onCta)
            }

            if (onBack != null) {
                BackButton(onClick = onBack)
            }

            content()

            if (showTerms) {
                TermsFooter()
            }
        }
    }
}

// The dual-logo header row: the NomNomz app mark tinted in the provider's brand colour, a muted separator
// glyph, then the provider's own logo in its brand colour.
@Composable
private fun DualLogoHeader(provider: ConnectProvider) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Icon(
            imageVector = NomNomzMarkGlyph,
            contentDescription = stringResource(Res.string.connect_logo_cd_app),
            tint = provider.brand.brand,
            modifier = Modifier.size(AppLogoSize),
        )
        // The "linking" separator between the two logos — decorative, but kept in the SR reading order so
        // the header announces "NomNomz logo, connect to, <provider> logo" rather than two adjacent logos.
        val separatorLabel: String = stringResource(Res.string.connect_modal_logo_separator_cd)
        Text(
            text = LOGO_SEPARATOR,
            style = typography.lg,
            color = tokens.mutedForeground,
            modifier =
                Modifier
                    .size(LogoSeparatorSize)
                    .clearAndSetSemantics { contentDescription = separatorLabel },
        )
        Icon(
            imageVector = provider.providerLogo,
            contentDescription = stringResource(provider.providerLogoDescription),
            tint = provider.brand.brand,
            modifier = Modifier.size(ProviderLogoSize),
        )
    }
}

// The full-width brand-coloured primary CTA: the provider logo + the CTA label, on the brand background with
// the brand-correct on-colour (white for Twitch/Discord/YouTube, black for Spotify).
// Uses a direct Foundation Row — not a catalogue Button — because the background is a per-provider brand
// colour that is NOT a design token, and the corner radius (xl=14dp) differs from Button's default (md=8dp).
@Composable
private fun BrandCta(provider: ConnectProvider, onClick: () -> Unit) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val ctaShape: RoundedCornerShape = RoundedCornerShape(CtaCornerRadius)
    val interactionSource: MutableInteractionSource = remember { MutableInteractionSource() }

    Row(
        modifier =
            Modifier
                .fillMaxWidth()
                .clip(ctaShape)
                .background(provider.brand.brand)
                .hoverable(interactionSource)
                .clickable(interactionSource = interactionSource, indication = null, onClick = onClick)
                .pointerHoverIcon(PointerIcon.Hand)
                .padding(horizontal = spacing.s4, vertical = spacing.s2),
        horizontalArrangement = Arrangement.spacedBy(spacing.s2, Alignment.CenterHorizontally),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Icon(
            imageVector = provider.providerLogo,
            contentDescription = null,
            tint = provider.brand.onBrand,
            modifier = Modifier.size(CtaIconSize),
        )
        Text(
            text = stringResource(provider.ctaLabel),
            style = typography.sm.copy(fontWeight = FontWeight.Medium),
            color = provider.brand.onBrand,
        )
    }
}

// The optional secondary "Back" button for per-provider connect flows: a dark, on-token outlined button
// with a back arrow — never the brand colour (the CTA owns the brand emphasis).
// Uses a direct Foundation Row for the same reason as BrandCta: custom corner radius (xl=14dp) and a
// specific border thickness that differ from catalogue Button defaults.
@Composable
private fun BackButton(onClick: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val ctaShape: RoundedCornerShape = RoundedCornerShape(CtaCornerRadius)
    val interactionSource: MutableInteractionSource = remember { MutableInteractionSource() }

    Row(
        modifier =
            Modifier
                .fillMaxWidth()
                .border(BorderStroke(spacing.s0_5 / 2, tokens.border), ctaShape)
                .clip(ctaShape)
                .hoverable(interactionSource)
                .clickable(interactionSource = interactionSource, indication = null, onClick = onClick)
                .pointerHoverIcon(PointerIcon.Hand)
                .padding(horizontal = spacing.s4, vertical = spacing.s2),
        horizontalArrangement = Arrangement.spacedBy(spacing.s2, Alignment.CenterHorizontally),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Icon(
            imageVector = BackArrowGlyph,
            contentDescription = null,
            tint = tokens.foreground,
            modifier = Modifier.size(BackIconSize),
        )
        Text(
            text = stringResource(Res.string.connect_modal_back),
            style = typography.sm.copy(fontWeight = FontWeight.Medium),
            color = tokens.foreground,
        )
    }
}

// The first-login footer: a muted Terms/Privacy line with the two links rendered in the accent colour.
// LinkedText paints embedded URLs in the accent token, so the copy carries the plain links inline.
@Composable
private fun TermsFooter() {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    LinkedText(
        text = stringResource(Res.string.connect_modal_terms),
        style = typography.xs,
        color = tokens.mutedForeground,
        modifier =
            Modifier
                .fillMaxWidth()
                .padding(top = spacing.s2),
    )
}

// Paint a soft, blurred-looking ambient radial glow in the provider's [brandColor] over the near-black
// backdrop — two offset, low-alpha radial gradients reading as out-of-focus coloured blobs. Decorative
// only (no semantics); drawn as the box background beneath the card.
private fun Modifier.ambientBrandGlow(brandColor: Color): Modifier =
    this
        .background(
            Brush.radialGradient(
                colors = listOf(brandColor.copy(alpha = GLOW_ALPHA_PRIMARY), Color.Transparent),
                center = Offset(GLOW_PRIMARY_CX, GLOW_PRIMARY_CY),
                radius = GLOW_PRIMARY_RADIUS,
            ),
        )
        .background(
            Brush.radialGradient(
                colors = listOf(brandColor.copy(alpha = GLOW_ALPHA_SECONDARY), Color.Transparent),
                center = Offset(GLOW_SECONDARY_CX, GLOW_SECONDARY_CY),
                radius = GLOW_SECONDARY_RADIUS,
            ),
        )

// The reference's two ambient blobs: a stronger top-left glow and a subtler lower-right one. Alphas are kept
// low so the effect stays dark and subtle (a tint over near-black, never a wash). These are layout/effect
// constants (positions in px, alphas), not design tokens — they parameterise the decorative glow only.
private const val GLOW_ALPHA_PRIMARY: Float = 0.22f
private const val GLOW_ALPHA_SECONDARY: Float = 0.14f
private const val GLOW_PRIMARY_CX: Float = 360f
private const val GLOW_PRIMARY_CY: Float = 300f
private const val GLOW_PRIMARY_RADIUS: Float = 900f
private const val GLOW_SECONDARY_CX: Float = 1100f
private const val GLOW_SECONDARY_CY: Float = 1200f
private const val GLOW_SECONDARY_RADIUS: Float = 1100f

private const val LOGO_SEPARATOR: String = "×"
