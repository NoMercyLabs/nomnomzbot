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
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import kotlinx.coroutines.launch

/**
 * The inline "upload a new one" action every Library-backed picker (sound clip, file) offers beside its list. It
 * is the quiet outline variant: the picker's own selection is the primary job, the upload is the way out when the
 * wanted item does not exist yet. The upload itself, and selecting the result, follow [uploadAndSelect].
 */
@Composable
fun PickerUploadButton(
    label: String,
    upload: suspend () -> String?,
    onUploaded: (String) -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    val scope = rememberCoroutineScope()
    var uploading: Boolean by remember { mutableStateOf(false) }

    Button(
        onClick = {
            scope.launch {
                uploading = true
                try {
                    uploadAndSelect(upload = upload, select = onUploaded)
                } finally {
                    uploading = false
                }
            }
        },
        modifier = modifier,
        variant = ButtonVariant.Outline,
        size = ButtonSize.Sm,
        enabled = enabled,
        loading = uploading,
    ) {
        Text(text = label)
    }
}
