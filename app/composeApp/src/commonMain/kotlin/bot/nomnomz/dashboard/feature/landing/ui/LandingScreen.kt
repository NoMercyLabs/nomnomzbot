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
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.windowInsetsPadding
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
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.connect.ui.NomNomzMarkGlyph
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.app_name
import nomnomzbot.composeapp.generated.resources.landing_get_started
import nomnomzbot.composeapp.generated.resources.landing_heading
import nomnomzbot.composeapp.generated.resources.landing_subtitle
import org.jetbrains.compose.resources.stringResource

// The public front page (Destination.Landing) shown to a booted-but-not-connected visitor before the
// sign-in card. A minimal, on-token hero: the NomNomz mark (tinted in the dynamic accent), a bold heading,
// a short muted subtitle, and a single "Get started" primary CTA that advances the gate to Connect. Purely
// presentational — it owns no state; the host wires [onGetStarted] to the destination transition.
//
// The centred text block is capped at a comfortable reading width. Like [ConnectModal]'s card constants,
// this is a layout constant the neutral spacing scale doesn't cover — not a design token.
private val ContentMaxWidth = 420.dp

@Composable
fun LandingScreen(onGetStarted: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(
        modifier =
            Modifier
                .fillMaxSize()
                .background(tokens.background)
                .windowInsetsPadding(WindowInsets.safeDrawing)
                .padding(spacing.s6),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier.widthIn(max = ContentMaxWidth).fillMaxWidth(),
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
}
