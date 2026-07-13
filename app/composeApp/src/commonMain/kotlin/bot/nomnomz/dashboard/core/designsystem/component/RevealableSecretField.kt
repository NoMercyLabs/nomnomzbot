// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.setup_secret_hide
import nomnomzbot.composeapp.generated.resources.setup_secret_show
import org.jetbrains.compose.resources.stringResource

// A masked text field for secrets — client secrets, tokens, keys — hidden by default with a joined Show/Hide
// toggle to reveal it. So a streamer can't leak a key on camera but can still check what they pasted. This is the
// single source of the reveal affordance shared by the setup wizard and the integrations credential card (the
// same masking + Show/Hide logic was duplicated in both before this). Caller passes the contextual [label] /
// [supportingText]; the component owns only the intrinsic reveal toggle.
@Composable
fun RevealableSecretField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    supportingText: String? = null,
    isError: Boolean = false,
    errorText: String? = null,
) {
    var revealed: Boolean by remember { mutableStateOf(false) }

    AppTextField(
        value = value,
        onValueChange = onValueChange,
        label = label,
        modifier = modifier,
        enabled = enabled,
        isError = isError,
        errorText = errorText,
        supportingText = supportingText,
        visualTransformation =
            if (revealed) VisualTransformation.None else PasswordVisualTransformation(),
        trailingIcon = {
            TextButton(onClick = { revealed = !revealed }, enabled = enabled) {
                Text(
                    stringResource(
                        if (revealed) Res.string.setup_secret_hide else Res.string.setup_secret_show
                    )
                )
            }
        },
    )
}
