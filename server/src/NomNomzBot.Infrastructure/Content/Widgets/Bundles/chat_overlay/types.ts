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

// How a reply the bot put on the overlay looks (the wire OverlayLineDto): who asked, the highlight as a CSS color
// (resolved from a design token; '' = none), a size and how long it stays (0 = the widget's own show time).
export interface LineLook {
  requestedBy: string
  highlight: string
  color: string
  large: boolean
  durationSeconds: number
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
  // Set only on a bot overlay line (a command reply sent to the overlay); null on every other line.
  look: LineLook | null
}
