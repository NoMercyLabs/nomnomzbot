// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { NowPlayingState } from "./state.js";
import { loadIconMarkup } from "./iconAssets.js";

const SIZE = 144;
/** Icons are drawn on a 24x24 source viewBox scaled 3x (=72px) inside the 144px key, leaving an
 * even 36px (25%) margin on every side — the "a little padding" every key face needs. */
const ICON_SCALE = 3;
const ICON_OFFSET = (SIZE - 24 * ICON_SCALE) / 2;

/** The default key background — every action's Property Inspector exposes a color picker to override it. */
export const DEFAULT_BACKGROUND = "#1a1a1a";

/** Play/Pause's icon sits in the top ~2/3 of the key (smaller than the other keys' icons) so the
 * elapsed-time text has clear, non-overlapping room below it — both centered on x=72 independently
 * of each other's height, not hand-placed. */
const PLAY_PAUSE_ICON_SCALE = 2.5;
const PLAY_PAUSE_ICON_TOP = 28;
const PLAY_PAUSE_ICON_OFFSET_X = (SIZE - 24 * PLAY_PAUSE_ICON_SCALE) / 2;

/**
 * Renders the Play/Pause key as an inline SVG data URI — icon + live elapsed time in one image
 * (streamdeck-plugin.md P4). SVG rather than a canvas-rasterized PNG: the Elgato SDK's `setImage`
 * accepts `data:image/svg+xml` directly, and SVG needs no native image library / build toolchain,
 * which a canvas rasterizer would (a real constraint on a fresh install — no bundled Cairo/Skia).
 * Reuses the real play/pause icon files (loadIconMarkup) through the same centered-transform
 * approach every other key uses, instead of hand-drawn shapes.
 */
export function renderPlayPauseKey(state: NowPlayingState, backgroundColor: string = DEFAULT_BACKGROUND): string {
  const isPlaying = state.current?.isPlaying ?? false;
  const source = loadIconMarkup(isPlaying ? "pause" : "play");
  const inner = source.replace(/^<svg[^>]*>/, "").replace(/<\/svg>\s*$/, "");
  const time = escapeXml(state.formattedElapsed());
  // Whichever action the icon currently offers (pause while playing, resume while paused) is the one
  // that matters for dimming — the other direction's flag is irrelevant right now.
  const blocked = isPlaying ? state.current?.canPause === false : state.current?.canResume === false;
  const opacity = blocked ? 0.35 : 1;

  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${SIZE}" height="${SIZE}">
    <rect width="${SIZE}" height="${SIZE}" rx="24" fill="${backgroundColor}"/>
    <g transform="translate(${PLAY_PAUSE_ICON_OFFSET_X},${PLAY_PAUSE_ICON_TOP}) scale(${PLAY_PAUSE_ICON_SCALE})" opacity="${opacity}">${inner}</g>
    <text x="${SIZE / 2}" y="126" font-family="sans-serif" font-size="34" font-weight="bold" fill="#fff" text-anchor="middle">${time}</text>
  </svg>`;

  return `data:image/svg+xml;base64,${Buffer.from(svg).toString("base64")}`;
}

/**
 * Renders any other action's key: the recolored (white) source icon, padded and centered, over a
 * configurable background — the generic renderer every non-live action goes through, so "padding"
 * and "background color" are one mechanism instead of a per-action special case.
 */
/** `dimmed` reflects a PROVIDER-side restriction (Spotify's `actions.disallows`, an ad break/restricted
 * market/non-Premium account) rather than the icon's own on/off state — lowers opacity so a blocked
 * control reads as unavailable at a glance instead of failing silently on the next press. */
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

const MARQUEE_FONT_SIZE = 16;
const MARQUEE_CHAR_WIDTH = MARQUEE_FONT_SIZE * 0.56;
const MARQUEE_VIEW_X = 8;
const MARQUEE_VIEW_Y = 126;
const MARQUEE_VIEW_WIDTH = 128;
const MARQUEE_GAP = "     ";
const MARQUEE_PX_PER_TICK = 4;

/**
 * Renders the "Now Playing" key: real cover art (fetched/cached separately — see coverArt.ts, `image`
 * needs a self-contained data URI since `setImage` can't reference an external URL), a bottom-of-key
 * gradient for text legibility over any artwork, and a title/artist marquee that scrolls when it's too
 * wide to fit — advanced by `tick` (the caller's own render-loop counter), not real time, so this stays
 * a pure function.
 */
export function renderNowPlayingKey(
  state: NowPlayingState,
  coverArtDataUri: string | null,
  tick: number,
  backgroundColor: string = DEFAULT_BACKGROUND,
): string {
  const np = state.current;
  const title = np?.title?.trim() || "Nothing playing";
  const artist = np?.artist?.trim() || "";
  const label = artist ? `${title} — ${artist}` : title;

  const imageEl = coverArtDataUri
    ? `<image href="${coverArtDataUri}" x="0" y="0" width="${SIZE}" height="${SIZE}" preserveAspectRatio="xMidYMid slice" clip-path="url(#roundedClip)"/>`
    : "";

  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${SIZE}" height="${SIZE}">
    <defs>
      <clipPath id="roundedClip"><rect width="${SIZE}" height="${SIZE}" rx="24"/></clipPath>
      <clipPath id="marqueeClip"><rect x="${MARQUEE_VIEW_X}" y="104" width="${MARQUEE_VIEW_WIDTH}" height="32"/></clipPath>
      <linearGradient id="fade" x1="0" y1="0" x2="0" y2="1">
        <stop offset="0%" stop-color="#000" stop-opacity="0"/>
        <stop offset="100%" stop-color="#000" stop-opacity="0.8"/>
      </linearGradient>
    </defs>
    <rect width="${SIZE}" height="${SIZE}" rx="24" fill="${backgroundColor}"/>
    ${imageEl}
    <rect x="0" y="84" width="${SIZE}" height="60" fill="url(#fade)" clip-path="url(#roundedClip)"/>
    <g clip-path="url(#marqueeClip)">${renderMarquee(label, tick)}</g>
  </svg>`;

  return `data:image/svg+xml;base64,${Buffer.from(svg).toString("base64")}`;
}

function renderMarquee(label: string, tick: number): string {
  const textWidth = label.length * MARQUEE_CHAR_WIDTH;
  if (textWidth <= MARQUEE_VIEW_WIDTH) {
    return `<text x="${MARQUEE_VIEW_X + MARQUEE_VIEW_WIDTH / 2}" y="${MARQUEE_VIEW_Y}" font-family="sans-serif" font-size="${MARQUEE_FONT_SIZE}" font-weight="600" fill="#fff" text-anchor="middle">${escapeXml(label)}</text>`;
  }

  const loopWidthPx = textWidth + MARQUEE_GAP.length * MARQUEE_CHAR_WIDTH;
  const offset = -((tick * MARQUEE_PX_PER_TICK) % loopWidthPx);
  const looped = `${label}${MARQUEE_GAP}${label}`;
  return `<text x="${MARQUEE_VIEW_X + offset}" y="${MARQUEE_VIEW_Y}" font-family="sans-serif" font-size="${MARQUEE_FONT_SIZE}" font-weight="600" fill="#fff">${escapeXml(looped)}</text>`;
}

function escapeXml(value: string): string {
  return value.replace(/[<>&'"]/g, (char) => {
    switch (char) {
      case "<":
        return "&lt;";
      case ">":
        return "&gt;";
      case "&":
        return "&amp;";
      case "'":
        return "&apos;";
      default:
        return "&quot;";
    }
  });
}
