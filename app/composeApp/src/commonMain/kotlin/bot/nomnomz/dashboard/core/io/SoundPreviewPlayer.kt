// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.io

/**
 * Plays a sound clip in the dashboard itself (not the OBS overlay) so the operator hears a Preview immediately —
 * the Preview button was previously only pushing the clip to the overlay, so clicking it was silent unless OBS
 * happened to be open. [url] is the clip's relative, anonymous, range-enabled stream URL (SoundClip.previewUrl).
 * Fire-and-forget best-effort playback; failures are swallowed so a Preview click never throws.
 */
expect fun playSoundPreview(url: String)
