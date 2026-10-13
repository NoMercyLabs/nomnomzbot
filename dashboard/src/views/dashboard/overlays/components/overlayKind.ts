// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { Component } from 'vue'
import BellNotification from '~icons/nnz/bell-notification'
import BellRing from '~icons/nnz/bell-ring'
import ChatMessages from '~icons/nnz/chat-messages'
import ClosedCaption from '~icons/nnz/closed-caption'
import Medal from '~icons/nnz/medal'
import Monitor from '~icons/nnz/monitor'
import NoteEighthPair from '~icons/nnz/note-eighth-pair'
import PictureInPicture from '~icons/nnz/picture-in-picture'
import Speaker from '~icons/nnz/speaker'
import Trophy from '~icons/nnz/trophy'
import WaveForm from '~icons/nnz/wave-form'

export interface OverlayKind {
  /** Key under dashboard.overlays.kinds. */
  key: string
  icon: Component
}

// The bot stores no kind for an overlay, so the kind is read from its name; an unknown name is a plain overlay.
const KINDS: Readonly<Record<string, OverlayKind>> = {
  'Twitch Chat Overlay': { key: 'chat', icon: ChatMessages },
  'Spotify NowPlaying': { key: 'nowPlaying', icon: NoteEighthPair },
  'Logo badge': { key: 'badge', icon: Medal },
  'TTS Audio Widget': { key: 'tts', icon: WaveForm },
  BSOD: { key: 'fullScreen', icon: Monitor },
  Leaderboard: { key: 'leaderboard', icon: Trophy },
  'Feather Alert': { key: 'alert', icon: BellNotification },
  'TTS Caption': { key: 'caption', icon: ClosedCaption },
  Alerts: { key: 'alerts', icon: BellRing },
  Audio: { key: 'audio', icon: Speaker },
}

const FALLBACK: OverlayKind = { key: 'overlay', icon: PictureInPicture }

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
