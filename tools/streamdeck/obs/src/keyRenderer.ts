// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { loadIconMarkup } from "./iconAssets.js";

const SIZE = 144;
/** Icons are drawn on a 24x24 source viewBox scaled 3x (=72px) inside the 144px key, leaving an
 * even 36px (25%) margin on every side — the "a little padding" every key face needs. */
const ICON_SCALE = 3;
const ICON_OFFSET = (SIZE - 24 * ICON_SCALE) / 2;

/** The default key background — every action's Property Inspector exposes a color picker to override it. */
export const DEFAULT_BACKGROUND = "#1a1a1a";

/**
 * Renders an action's key: the recolored (white) source icon, padded and centered, over a
 * configurable background — the generic renderer every OBS action goes through, so "padding"
 * and "background color" are one mechanism instead of a per-action special case.
 */
/** `dimmed` reflects a source/state-side restriction (e.g. a filter or input that can't currently be
 * toggled) rather than the icon's own on/off state — lowers opacity so a blocked control reads as
 * unavailable at a glance instead of failing silently on the next press. */
export function renderIconKey(
  iconName: string,
  backgroundColor: string = DEFAULT_BACKGROUND,
  dimmed: boolean = false,
): string {
  const source = loadIconMarkup(iconName);
  const inner = source.replace(/^<svg[^>]*>/, "").replace(/<\/svg>\s*$/, "");
  const opacity = dimmed ? 0.35 : 1;

  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${SIZE}" height="${SIZE}">
    <rect width="${SIZE}" height="${SIZE}" rx="24" fill="${backgroundColor}"/>
    <g transform="translate(${ICON_OFFSET},${ICON_OFFSET}) scale(${ICON_SCALE})" opacity="${opacity}">${inner}</g>
  </svg>`;

  return `data:image/svg+xml;base64,${Buffer.from(svg).toString("base64")}`;
}
