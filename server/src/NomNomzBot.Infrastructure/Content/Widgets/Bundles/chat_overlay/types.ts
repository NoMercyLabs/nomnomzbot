// SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs
// The shapes the chat overlay passes between its components. The wire types (NnzChatFragmentDto,
// NnzChatPaintDto, ...) are the typed SDK globals the widget nnz.d.ts declares.

export interface Features {
  userBadges: boolean
  userPronouns: boolean
  userColors: boolean
  lightColorDetection: boolean
  shineEffect: boolean
  ogPreviews: boolean
  emoteSupport: boolean
}

// A processed song request, drawn instead of a text body.
export interface SongCardData {
  title: string
  description: string
  imageUrl: string
  linkUrl: string
  provider: string
}

export interface ChatLine {
  id: string
  sourceId: string
  userId: string
  name: string
  color: string
  pronouns: string
  avatarUrl: string
  badgeUrls: string[]
  fragments: NnzChatFragmentDto[]
  message: string
  emoteOnly: boolean
  state: string
  // Set only on a song-request line.
  card: SongCardData | null
  // The chatter's 7TV name-theme paint, already flattened to CSS by the server; null when they wear none.
  paint: NnzChatPaintDto | null
}
