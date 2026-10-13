// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import {
  AudioLines,
  BadgeCheck,
  BellRing,
  Captions,
  Feather,
  Layers,
  MessagesSquare,
  MonitorX,
  Music,
  Trophy,
  Volume2,
} from '@lucide/vue'
import type { Component } from 'vue'

export interface OverlayKind {
  /** Key under dashboard.overlays.kinds. */
  key: string
  icon: Component
}

// The bot stores no kind for an overlay, so the kind is read from its name; an unknown name is a plain overlay.
const KINDS: Readonly<Record<string, OverlayKind>> = {
  'Twitch Chat Overlay': { key: 'chat', icon: MessagesSquare },
  'Spotify NowPlaying': { key: 'nowPlaying', icon: Music },
  'Logo badge': { key: 'badge', icon: BadgeCheck },
  'TTS Audio Widget': { key: 'tts', icon: AudioLines },
  BSOD: { key: 'fullScreen', icon: MonitorX },
  Leaderboard: { key: 'leaderboard', icon: Trophy },
  'Feather Alert': { key: 'alert', icon: Feather },
  'TTS Caption': { key: 'caption', icon: Captions },
  Alerts: { key: 'alerts', icon: BellRing },
  Audio: { key: 'audio', icon: Volume2 },
}

const FALLBACK: OverlayKind = { key: 'overlay', icon: Layers }

export function overlayKind(name: string): OverlayKind {
  return KINDS[name] ?? FALLBACK
}

// "channel.subscription.gift" reads as "subscription gift".
export function plainEvent(key: string): string {
  return key.replace(/^(twitch|channel|overlay|spotify)\./, '').replace(/[._]/g, ' ')
}

// The address with its query cut off: the token travels in the query and must never be shown.
export function maskedAddress(url: string): string {
  try {
    const parsed: URL = new URL(url)
    return `${parsed.origin}${parsed.pathname}`
  } catch {
    return url.split('?')[0] ?? ''
  }
}
