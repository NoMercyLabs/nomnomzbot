// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { ChatMessagePayload } from '@/realtime/payloads'

// The live feed of one channel, oldest line first. The hub appends to it; the page seeds it from the history call.
export const chatKey = (channelId: string | null): unknown[] => ['channels', channelId, 'chat']

export const chatLineLimit = 500

// Why a line is no longer shown as normal: one message deleted, a person's lines purged, or the whole chat cleared.
export type ChatLineRemoval = 'deleted' | 'purged' | 'cleared'

export type ChatLine = ChatMessagePayload & { removed?: ChatLineRemoval }
