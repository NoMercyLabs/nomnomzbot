// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.landing.ui

import androidx.compose.foundation.background
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
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.icon.AppIcon
import bot.nomnomz.dashboard.core.designsystem.icon.AppIcons
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography
import bot.nomnomz.dashboard.feature.connect.ui.NomNomzMarkGlyph
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.app_name
import nomnomzbot.composeapp.generated.resources.landing_feature_community_body
import nomnomzbot.composeapp.generated.resources.landing_feature_community_title
import nomnomzbot.composeapp.generated.resources.landing_feature_economy_body
import nomnomzbot.composeapp.generated.resources.landing_feature_economy_title
import nomnomzbot.composeapp.generated.resources.landing_feature_integrations_body
import nomnomzbot.composeapp.generated.resources.landing_feature_integrations_title
import nomnomzbot.composeapp.generated.resources.landing_feature_pipelines_body
import nomnomzbot.composeapp.generated.resources.landing_feature_pipelines_title
import nomnomzbot.composeapp.generated.resources.landing_feature_platforms_body
import nomnomzbot.composeapp.generated.resources.landing_feature_platforms_title
import nomnomzbot.composeapp.generated.resources.landing_feature_tts_body
import nomnomzbot.composeapp.generated.resources.landing_feature_tts_title
import nomnomzbot.composeapp.generated.resources.landing_feature_widgets_body
import nomnomzbot.composeapp.generated.resources.landing_feature_widgets_title
import nomnomzbot.composeapp.generated.resources.landing_features_heading
import nomnomzbot.composeapp.generated.resources.landing_get_started
import nomnomzbot.composeapp.generated.resources.landing_heading
import nomnomzbot.composeapp.generated.resources.landing_subtitle
import org.jetbrains.compose.resources.DrawableResource
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The public front page (Destination.Landing) shown to a booted-but-not-connected visitor before the
// sign-in card. An on-token hero — the NomNomz mark (tinted in the dynamic accent), a bold heading, a
// short muted subtitle, and a single "Get started" primary CTA that advances the gate to Connect — followed
// by a features section naming the major capability groups the product actually ships today (§5 of the
// 2026-09-08 owner punch list: the page previously described almost nothing). Purely presentational — it
// owns no state; the host wires [onGetStarted] to the destination transition.
//
// Sleak: the CTA stays the page's one primary action. The feature section below it is plain text inside
// [Card]s — no buttons, no accent color — so it supports the CTA instead of competing with it for weight.
//
// The centred text block is capped at a comfortable reading width. Like [ConnectModal]'s card constants,
// this is a layout constant the neutral spacing scale doesn't cover — not a design token.
private val ContentMaxWidth = 420.dp

/** One entry in the features section: a catalogue icon plus a title/body string pair. */
private data class LandingFeature(
    val icon: DrawableResource,
    val titleRes: StringResource,
    val bodyRes: StringResource,
)

// The seven capability groups from the owner punch list, each verified against the real feature/*
// screens and network clients before this copy was written — nothing here is planned or "coming soon".
private val LandingFeatures: List<LandingFeature> =
    listOf(
        LandingFeature(
            icon = AppIcons.GridInterfaceHeader,
            titleRes = Res.string.landing_feature_platforms_title,
            bodyRes = Res.string.landing_feature_platforms_body,
        ),
        LandingFeature(
            icon = AppIcons.BotFlow,
            titleRes = Res.string.landing_feature_pipelines_title,
            bodyRes = Res.string.landing_feature_pipelines_body,
        ),
        LandingFeature(
            icon = AppIcons.UsersGroup,
            titleRes = Res.string.landing_feature_community_title,
            bodyRes = Res.string.landing_feature_community_body,
        ),
        LandingFeature(
            icon = AppIcons.Speaker,
            titleRes = Res.string.landing_feature_tts_title,
            bodyRes = Res.string.landing_feature_tts_body,
        ),
        LandingFeature(
            icon = AppIcons.Coins,
            titleRes = Res.string.landing_feature_economy_title,
            bodyRes = Res.string.landing_feature_economy_body,
        ),
        LandingFeature(
            icon = AppIcons.ConnectingCable,
            titleRes = Res.string.landing_feature_integrations_title,
            bodyRes = Res.string.landing_feature_integrations_body,
        ),
        LandingFeature(
            icon = AppIcons.MonitorDisplayStand,
            titleRes = Res.string.landing_feature_widgets_title,
            bodyRes = Res.string.landing_feature_widgets_body,
        ),
    )

@Composable
fun LandingScreen(onGetStarted: () -> Unit) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current

    Box(
        modifier =
            Modifier
                .fillMaxSize()
                .background(tokens.background)
                .windowInsetsPadding(WindowInsets.safeDrawing),
    ) {
        Column(
            modifier =
                Modifier
                    .fillMaxSize()
                    .verticalScroll(rememberScrollState())
                    .padding(spacing.s6),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Column(
                modifier = Modifier.widthIn(max = ContentMaxWidth).fillMaxWidth(),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(spacing.s12),
            ) {
                LandingHero(onGetStarted = onGetStarted)
                LandingFeaturesSection()
            }
        }
    }
}

@Composable
private fun LandingHero(onGetStarted: () -> Unit) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    Column(
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(spacing.s6),
    ) {
        Icon(
            imageVector = NomNomzMarkGlyph,
            contentDescription = stringResource(Res.string.app_name),
            tint = tokens.primary,
            modifier = Modifier.size(spacing.s16),
        )

        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = stringResource(Res.string.landing_heading),
                style = typography.xl2.copy(fontWeight = FontWeight.Bold),
                color = tokens.foreground,
                textAlign = TextAlign.Center,
            )
            Text(
                text = stringResource(Res.string.landing_subtitle),
                style = typography.sm,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
        }

        Button(onClick = onGetStarted, size = ButtonSize.Lg) {
            Text(stringResource(Res.string.landing_get_started))
        }
    }
}

@Composable
private fun LandingFeaturesSection() {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth(),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Text(
            text = stringResource(Res.string.landing_features_heading),
            style = typography.lg.copy(fontWeight = FontWeight.SemiBold),
            color = tokens.foreground,
            textAlign = TextAlign.Center,
        )

        Column(
            modifier = Modifier.fillMaxWidth(),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            LandingFeatures.forEach { feature -> LandingFeatureRow(feature) }
        }
    }
}

@Composable
private fun LandingFeatureRow(feature: LandingFeature) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            AppIcon(
                icon = feature.icon,
                contentDescription = null,
                tint = tokens.foreground,
                size = spacing.s6,
            )
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(
                    text = stringResource(feature.titleRes),
                    style = typography.base.copy(fontWeight = FontWeight.SemiBold),
                    color = tokens.foreground,
                )
                Text(
                    text = stringResource(feature.bodyRes),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            }
        }
    }
}
