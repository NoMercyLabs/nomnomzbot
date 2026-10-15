<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) comes from this widget's own SDK types. A chat payload is read as a loose record and each
// field is narrowed where it is read, so one bad field never breaks a line.
type Payload = Record<string, unknown>

function asPayload(data: unknown): Payload {
  return data !== null && typeof data === 'object' ? (data as Payload) : {}
}

// Renders the decorated chat DTO ("ChatMessage" — the camelCase DashboardChatMessageDto shape the
// ChatMessageBroadcastHandler pushes to overlays: fragments with resolved emote urls, badges, colour,
// avatar, pronouns). Idle until messages arrive.
interface ChatBoxConfig {
  theme: string          // 'dark' | 'light' | 'transparent'
  maxMessages: number
  fadeAfterMs: number    // 0 = never fade
  showBadges: boolean
  showEmotes: boolean
  showSevenTvPaints: boolean // false = names fall back to plain chat colour even when a paint resolved
  hideCommands: boolean  // drop messages starting with '!'
  hideBots: boolean      // drop well-known bot accounts
  accentColor: string
  fontFamily: string     // '' = system default
  fontSize: number       // px
  background: string     // '' = use the theme's line background; a hex overrides it
  backgroundOpacity: number // 0..1, applied to the background override
  showTimestamps: boolean
}

const cfg = reactive<ChatBoxConfig>({
  theme: 'dark',
  maxMessages: 12,
  fadeAfterMs: 0,
  showBadges: true,
  showEmotes: true,
  showSevenTvPaints: true,
  hideCommands: true,
  hideBots: true,
  accentColor: '#9146ff',
  fontFamily: '',
  fontSize: 16,
  background: '',
  backgroundOpacity: 0.82,
  showTimestamps: false,
})

// A hex (#RGB or #RRGGBB) + opacity → an rgba() string, so the streamer can set any line background.
function hexToRgba(hex: string, opacity: number): string {
  const h: string = hex.trim().replace('#', '')
  const full: string = h.length === 3 ? h.split('').map((c) => c + c).join('') : h
  const r: number = parseInt(full.slice(0, 2), 16)
  const g: number = parseInt(full.slice(2, 4), 16)
  const b: number = parseInt(full.slice(4, 6), 16)
  const a: number = Math.min(1, Math.max(0, opacity))
  return `rgba(${r}, ${g}, ${b}, ${a})`
}

// WCAG 2.x relative luminance of a #RGB / #RRGGBB / #RRGGBBAA colour (alpha ignored).
function hexLuminance(hex: string): number {
  const h: string = hex.replace('#', '')
  const full: string = h.length === 3 ? h.split('').map((c) => c + c).join('') : h
  const channels: number[] = [0, 2, 4].map((i) => {
    const v: number = parseInt(full.slice(i, i + 2), 16) / 255
    return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4)
  })
  return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2]
}

// WCAG contrast ratio between two relative luminances.
function contrastRatio(a: number, b: number): number {
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
}

// Black or white, whichever reads better on EVERY colour of the bar: a gradient is judged by its worst stop,
// so the text stays legible across the whole bar rather than only at its start.
function readableTextColor(hexes: string[]): string {
  const lums: number[] = hexes.map(hexLuminance)
  const worst = (textLum: number): number => Math.min(...lums.map((l) => contrastRatio(l, textLum)))
  return worst(0) >= worst(1) ? '#000000' : '#ffffff'
}

// The colours the header bar is made of: the paint's own colours while paints are on (every hex stop of a
// gradient, or its flat colour), otherwise the chatter's Twitch name colour. Empty when there is none.
function headColors(l: ChatLine): string[] {
  if (cfg.showSevenTvPaints && l.paint) {
    const stops: string[] = (l.paint.backgroundImage || '').match(/#[0-9a-fA-F]{3,8}\b/g) || []
    if (stops.length) return stops
    if (l.paint.color) return [l.paint.color]
  }
  return l.color ? [l.color] : []
}

// The message header bar carries the chatter's colour: a resolved 7TV paint (gradient or flat) wins while
// showSevenTvPaints is on, then the Twitch name colour. The name and pronoun text on it are plain black or
// white by contrast (never painted themselves). An image-only paint has no known colours, so it keeps its
// picture and takes white text with a shadow. No colour at all leaves the header unstyled.
function headStyle(l: ChatLine): Record<string, string> {
  const painted: boolean = cfg.showSevenTvPaints && !!l.paint
  const image: string = painted && l.paint?.backgroundImage ? l.paint.backgroundImage : ''
  const colors: string[] = headColors(l)
  if (!colors.length && !image) return {}

  const style: Record<string, string> = {}
  if (image) {
    style['background-image'] = image
    // A gradient is applied exactly as 7TV defines it (no size/position/repeat overrides); only a raster
    // picture needs to be fitted to the bar.
    if (image.startsWith('url(')) {
      style['background-size'] = 'cover'
      style['background-position'] = 'center'
      style['background-repeat'] = 'no-repeat'
    }
  }
  if (!image || image.startsWith('url(')) {
    if (colors.length) style['background-color'] = colors[0]
  }
  const text: string = colors.length ? readableTextColor(colors) : '#ffffff'
  style.color = text
  style['-webkit-text-fill-color'] = text
  if (!colors.length) style['text-shadow'] = '0 1px 3px rgba(0, 0, 0, 0.9)'
  return style
}

const rootStyle = computed<Record<string, string>>(() => {
  const style: Record<string, string> = { '--accent': cfg.accentColor, 'font-size': cfg.fontSize + 'px' }
  if (cfg.fontFamily) style['font-family'] = cfg.fontFamily
  return style
})

// An explicit background hex overrides the theme's line background (inline styles beat the theme class).
const lineStyle = computed<Record<string, string>>(() => {
  const style: Record<string, string> = {}
  if (hexColor(cfg.background)) style.background = hexToRgba(cfg.background, cfg.backgroundOpacity)
  return style
})

function clockLabel(iso: unknown): string {
  const d: Date = new Date(String(iso || ''))
  if (isNaN(d.getTime())) return ''
  const hh: string = String(d.getHours()).padStart(2, '0')
  const mm: string = String(d.getMinutes()).padStart(2, '0')
  return hh + ':' + mm
}

const KNOWN_BOTS: string[] = ['nightbot', 'streamelements', 'streamlabs', 'moobot', 'fossabot', 'wizebot']

interface LinkCard {
  linkUrl?: string
  title?: string
  description?: string
  imageUrl?: string
  provider?: string
}

// The chatter's 7TV name-theme paint (ChatPaintDto), already flattened to CSS by the server. Absent — not an
// empty object — for a viewer wearing none, so 'no paint' never emits a stray style attribute.
interface ChatPaint {
  backgroundImage: string | null
  color: string | null
  textShadow: string | null
  isImageOnly: boolean
}

// One fragment of a chat message (ChatFragmentDto): the template reads the member that matches `type`.
interface ChatFragment {
  type: string
  text?: string
  emote?: { urls?: Record<string, string> } | null
  cheermote?: { urls?: Record<string, string>; colorHex?: string | null; bits?: number } | null
  mention?: { color?: string | null; displayName?: string | null; username?: string | null } | null
  linkUrl?: string | null
  linkPreview?: { title?: string | null; description?: string | null; imageUrl?: string | null } | null
  gif?: { url?: string } | null
}

interface ChatLine {
  id: string
  // The RAW chat message id, unlike `id` which is suffixed with a sequence to stay unique when the same
  // message id arrives twice. Enrichment and a single-message delete arrive keyed on this one.
  sourceId: string
  // The chatter's internal user id — a targeted "clear this person's messages" (channel.chat.clear_user_messages)
  // removes every line matching this, not just one sourceId.
  userId: string
  // Set when the bot later learned what this line meant — a song request resolving to a real track. When
  // present it REPLACES the body: the point is not to show "!sr <query>" and a card, it is to show the track.
  card: LinkCard | null
  name: string
  color: string
  pronouns: string
  avatarUrl: string
  badgeUrls: string[]
  fragments: ChatFragment[]
  message: string
  time: string
  faded: boolean
  role: string          // 'broadcaster' | 'moderator' | 'vip' | 'subscriber' | '' — for role accent/badge
  isCheer: boolean
  bitsAmount: number
  replyUserName: string
  replyMessageBody: string
  provider: string      // 'twitch' | 'kick' | 'youtube'
  paint: ChatPaint | null
}

const PROVIDER_LABEL: Record<string, string> = { twitch: 'Twitch', kick: 'Kick', youtube: 'YouTube' }

// Highest-priority role wins the line accent when a user carries more than one — broadcaster and
// moderator/VIP/subscriber are not mutually exclusive on Twitch (a mod can also be a subscriber).
function resolveRole(m: Payload): string {
  if (m.isBroadcaster) return 'broadcaster'
  if (m.isModerator) return 'moderator'
  if (m.isVip) return 'vip'
  if (m.isSubscriber) return 'subscriber'
  return ''
}

const lines = ref<ChatLine[]>([])
let seq = 0
const fadeTimers: number[] = []

function hexColor(c: unknown): string {
  return (typeof c === 'string' && /^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/.test(c.trim())) ? c.trim() : ''
}

function firstUrl(urls: unknown, keys: string[]): string {
  if (!urls) return ''
  const map: Payload = urls as Payload
  for (const key of keys) if (map[key]) return String(map[key])
  return ''
}

function emoteUrl(fr: ChatFragment): string {
  return firstUrl(fr && fr.emote && fr.emote.urls, ['2', '1', '3'])
}

function cheermoteUrl(fr: ChatFragment): string {
  return firstUrl(fr && fr.cheermote && fr.cheermote.urls, ['2', '1', '3'])
}

// Twitch's native chat GIF (GIPHY-backed, Tier 2+ subscriber feature): the fragment already carries a
// directly-fetchable url — no separate resolve step, unlike media-share's clip/video lookups.
function gifUrl(fr: ChatFragment): string {
  return (fr && fr.gif && typeof fr.gif.url === 'string') ? fr.gif.url : ''
}

// Mention/cheermote may carry a #RRGGBB accent; guard it before binding to style so bad data can't inject CSS.
function fragColor(c: unknown): Record<string, string> {
  const hex: string = hexColor(c)
  return hex ? { color: hex } : {}
}

// A mention takes the mentioned user's own chat colour for its text and its chip tint (--mention-color); a user with
// no colour sets neither, so it inherits the line's text colour just like their own name does.
function mentionStyle(c: unknown): Record<string, string> {
  const hex: string = hexColor(c)
  return hex ? { color: hex, '--mention-color': hex } : {}
}

// A line already on screen turned out to mean something richer — a song request resolving to a real track.
// Matched on the RAW message id; an id we never rendered (the line already scrolled off, or this overlay
// started after it) simply finds nothing and is ignored.
function onEnriched(data: unknown): void {
  const e: Payload = asPayload(data)
  const id: string = e.messageId ? String(e.messageId) : ''
  if (!id) return

  const line: ChatLine | undefined = lines.value.find((l: ChatLine) => l.sourceId === id)
  if (!line) return

  line.card = {
    linkUrl: e.linkUrl ? String(e.linkUrl) : undefined,
    title: e.title ? String(e.title) : undefined,
    description: e.description ? String(e.description) : undefined,
    imageUrl: e.imageUrl ? String(e.imageUrl) : undefined,
    provider: e.provider ? String(e.provider) : undefined,
  }
}

function paintOf(value: unknown): ChatPaint | null {
  if (!value || typeof value !== 'object') return null
  const p: Payload = value as Payload
  return {
    backgroundImage: typeof p.backgroundImage === 'string' ? p.backgroundImage : null,
    color: typeof p.color === 'string' ? p.color : null,
    textShadow: typeof p.textShadow === 'string' ? p.textShadow : null,
    isImageOnly: !!p.isImageOnly,
  }
}

function onChat(data: unknown): void {
  if (!data || typeof data !== 'object') return
  const m: Payload = data as Payload
  const text: string = String(m.message || '')
  if (cfg.hideCommands && (m.isCommand || text.charAt(0) === '!')) return
  const login: string = String(m.username || '').toLowerCase()
  if (cfg.hideBots && KNOWN_BOTS.indexOf(login) !== -1) return

  const line: ChatLine = {
    id: String(m.id || '') + '-' + (++seq),
    sourceId: String(m.id || ''),
    userId: typeof m.userId === 'string' ? m.userId : '',
    card: null,
    name: String(m.displayName || m.username || 'Someone'),
    color: hexColor(m.color),
    pronouns: String(m.pronouns || ''),
    avatarUrl: typeof m.avatarUrl === 'string' ? m.avatarUrl : '',
    badgeUrls: cfg.showBadges
      ? (Array.isArray(m.badges) ? (m.badges as Payload[]) : []).map((b: Payload) => firstUrl(b.urls, ['2', '1', '4'])).filter((u: string) => !!u)
      : [],
    fragments: Array.isArray(m.fragments) ? (m.fragments as ChatFragment[]) : [],
    message: text,
    time: clockLabel(m.timestamp),
    faded: false,
    role: resolveRole(m),
    isCheer: !!m.isCheer,
    bitsAmount: isFinite(Number(m.bitsAmount)) ? Number(m.bitsAmount) : 0,
    replyUserName: typeof m.replyParentUserName === 'string' ? m.replyParentUserName : '',
    replyMessageBody: typeof m.replyParentMessageBody === 'string' ? m.replyParentMessageBody : '',
    provider: typeof m.provider === 'string' ? m.provider.toLowerCase() : 'twitch',
    paint: paintOf(m.paint),
  }
  const next: ChatLine[] = lines.value.concat([line])
  while (next.length > Math.max(1, cfg.maxMessages)) next.shift()
  lines.value = next

  if (cfg.fadeAfterMs > 0) {
    fadeTimers.push(window.setTimeout(() => { line.faded = true }, cfg.fadeAfterMs))
  }
}

// A moderator (or Twitch AutoMod) deleted ONE message — Twitch removes it from its own chat immediately, so a
// line this overlay kept rendering after that is the exact "unacceptable" gap being fixed here. Matched on the
// same RAW message id onEnriched already keys off; a line already scrolled off finds nothing and is a no-op.
function onMessageDeleted(data: unknown): void {
  const e: Payload = asPayload(data)
  const id: string = e.messageId ? String(e.messageId) : ''
  if (!id) return
  lines.value = lines.value.filter((l: ChatLine) => l.sourceId !== id)
}

// A whole-channel "Clear chat" — every currently-rendered line goes, not just future ones.
function onChatCleared(): void {
  lines.value = []
}

// A targeted per-chatter purge (channel.chat.clear_user_messages) — every line from that ONE person goes;
// everyone else's messages stay exactly as they were.
function onUserMessagesCleared(data: unknown): void {
  const e: Payload = asPayload(data)
  const targetUserId: string = e.targetUserId ? String(e.targetUserId) : ''
  if (!targetUserId) return
  lines.value = lines.value.filter((l: ChatLine) => l.userId !== targetUserId)
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.theme === 'string' && s.theme) cfg.theme = s.theme
    if (isFinite(Number(s.maxMessages)) && Number(s.maxMessages) > 0) cfg.maxMessages = Number(s.maxMessages)
    if (isFinite(Number(s.fadeAfterMs)) && Number(s.fadeAfterMs) >= 0) cfg.fadeAfterMs = Number(s.fadeAfterMs)
    if (typeof s.showBadges === 'boolean') cfg.showBadges = s.showBadges
    if (typeof s.showEmotes === 'boolean') cfg.showEmotes = s.showEmotes
    if (typeof s.showSevenTvPaints === 'boolean') cfg.showSevenTvPaints = s.showSevenTvPaints
    if (typeof s.hideCommands === 'boolean') cfg.hideCommands = s.hideCommands
    if (typeof s.hideBots === 'boolean') cfg.hideBots = s.hideBots
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
    if (typeof s.fontFamily === 'string') cfg.fontFamily = s.fontFamily
    if (isFinite(Number(s.fontSize)) && Number(s.fontSize) > 0) cfg.fontSize = Number(s.fontSize)
    if (typeof s.background === 'string') cfg.background = s.background
    if (isFinite(Number(s.backgroundOpacity)) && Number(s.backgroundOpacity) >= 0)
      cfg.backgroundOpacity = Number(s.backgroundOpacity)
    if (typeof s.showTimestamps === 'boolean') cfg.showTimestamps = s.showTimestamps
  })
  NomNomz.on('ChatMessage', onChat)
  NomNomz.on('ChatMessageEnriched', onEnriched)
  NomNomz.on('MessageDeleted', onMessageDeleted)
  NomNomz.on('ChatCleared', onChatCleared)
  NomNomz.on('UserMessagesCleared', onUserMessagesCleared)
})

onUnmounted(() => {
  fadeTimers.forEach((t: number) => window.clearTimeout(t))
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('ChatMessage', onChat)
  NomNomz.off('ChatMessageEnriched', onEnriched)
  NomNomz.off('MessageDeleted', onMessageDeleted)
  NomNomz.off('ChatCleared', onChatCleared)
  NomNomz.off('UserMessagesCleared', onUserMessagesCleared)
})
</script>

<template>
  <TransitionGroup tag="div" name="chat-line" class="nnz-chatbox" :class="'theme-' + cfg.theme" :style="rootStyle">
    <div
      v-for="l in lines"
      :key="l.id"
      class="line"
      :class="{ faded: l.faded, [`role-${l.role}`]: !!l.role, cheer: l.isCheer }"
      :style="lineStyle"
    >
      <span v-if="l.replyMessageBody" class="reply-preview">
        <span class="reply-arrow">↳</span>
        <span class="reply-user">{{ l.replyUserName }}</span>
        <span class="reply-body">{{ l.replyMessageBody }}</span>
      </span>
      <span class="head" :class="{ 'head-colored': !!headColors(l).length }" :style="headStyle(l)">
        <span v-if="cfg.showTimestamps && l.time" class="time">{{ l.time }}</span>
        <span v-if="l.provider !== 'twitch'" class="platform" :class="'platform-' + l.provider">{{ PROVIDER_LABEL[l.provider] || l.provider }}</span>
        <img v-if="l.avatarUrl" class="avatar" :src="l.avatarUrl" alt="">
        <img v-for="(b, i) in l.badgeUrls" :key="i" class="badge" :src="b" alt="">
        <span v-if="l.role" class="role-badge" :class="'role-badge-' + l.role">{{ l.role }}</span>
        <span class="name">{{ l.name }}</span>
        <span v-if="l.pronouns" class="pron">({{ l.pronouns }})</span>
        <span v-if="l.isCheer && l.bitsAmount > 0" class="line-cheer-bits">{{ l.bitsAmount }} bits</span>
      </span>
      <span class="body">
        <!-- The bot resolved what this line meant: show THAT, not the command the viewer typed. -->
        <a v-if="l.card" class="link-card" :href="l.card.linkUrl" target="_blank" rel="noopener noreferrer">
          <img v-if="l.card.imageUrl" class="link-card-art" :src="l.card.imageUrl" alt="">
          <span class="link-card-text">
            <span class="link-card-title">{{ l.card.title }}</span>
            <span v-if="l.card.description" class="link-card-sub">{{ l.card.description }}</span>
          </span>
        </a>
        <template v-else-if="l.fragments.length">
          <template v-for="(fr, i) in l.fragments" :key="i">
            <!-- Backend-sanitized rich HTML (e.g. a subscriber's <marquee>/formatting). Rendered as-is; the server
                 is the only place that turns text into an html fragment, and it sanitizes before it ever gets here. -->
            <span v-if="fr.type === 'html'" class="frag-html" v-html="fr.text"></span>
            <!-- Twitch / third-party emote image -->
            <img v-else-if="fr.type === 'emote' && cfg.showEmotes && fr.emote" class="emote" :src="emoteUrl(fr)" :alt="fr.text">
            <!-- Cheermote: the animated bits image plus its tier-coloured amount -->
            <template v-else-if="fr.type === 'cheermote' && fr.cheermote">
              <img v-if="cfg.showEmotes && cheermoteUrl(fr)" class="cheermote" :src="cheermoteUrl(fr)" :alt="fr.text">
              <span class="cheer-bits" :style="fragColor(fr.cheermote.colorHex)">{{ fr.cheermote.bits }}</span>
            </template>
            <!-- Twitch native chat GIF (GIPHY-backed, Tier 2+ sub feature): the fragment carries a real,
                 directly-fetchable url, so it renders inline like an emote — just at GIF scale, since it IS
                 the message's content rather than a small inline glyph. fr.text is Twitch's own caption/alt. -->
            <img v-else-if="fr.type === 'gif' && gifUrl(fr)" class="chat-gif" :src="gifUrl(fr)" :alt="fr.text">
            <!-- @mention: a highlighted chip always, additionally tinted with the mentioned user's chat colour
                 when known — a mention must stand out even with no known colour. -->
            <span v-else-if="fr.type === 'mention' && fr.mention" class="mention" :style="mentionStyle(fr.mention.color)">{{ '@' + (fr.mention.displayName || fr.mention.username || '') }}</span>
            <!-- Shared link. With an OpenGraph preview resolved it renders as a card rather than a bare url —
                 that preview was already being fetched and attached by the backend and then thrown away here,
                 so a pasted link showed as raw text no matter what the server had learned about it. -->
            <a v-else-if="fr.type === 'link' && fr.linkUrl && fr.linkPreview && fr.linkPreview.title"
               class="link-card" :href="fr.linkUrl" target="_blank" rel="noopener noreferrer">
              <img v-if="fr.linkPreview.imageUrl" class="link-card-art" :src="fr.linkPreview.imageUrl" alt="">
              <span class="link-card-text">
                <span class="link-card-title">{{ fr.linkPreview.title }}</span>
                <span v-if="fr.linkPreview.description" class="link-card-sub">{{ fr.linkPreview.description }}</span>
              </span>
            </a>
            <a v-else-if="fr.type === 'link' && fr.linkUrl" class="link" :href="fr.linkUrl" target="_blank" rel="noopener noreferrer">{{ fr.text || fr.linkUrl }}</a>
            <!-- Plain text (the default, escaped) -->
            <span v-else>{{ fr.text || '' }}</span>
          </template>
        </template>
        <template v-else>{{ l.message }}</template>
      </span>
    </div>
  </TransitionGroup>
</template>

<style scoped>
.nnz-chatbox {
  position: fixed;
  left: 16px;
  bottom: 16px;
  width: min(420px, 40vw);
  display: flex;
  flex-direction: column;
  justify-content: flex-end;
  gap: 8px;
  pointer-events: none;
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  font-size: 16px;
  line-height: 1.5;
}
.line {
  padding: 8px 12px;
  border-radius: 10px;
  word-break: break-word;
  opacity: 1;
  transition: opacity 0.6s ease;
}
.line.faded {
  opacity: 0;
}
.theme-dark .line {
  color: #fff;
  background: rgba(12, 12, 18, 0.82);
  border: 1px solid color-mix(in srgb, var(--accent, #9146ff) 30%, transparent);
}
.theme-light .line {
  color: #17171d;
  background: rgba(255, 255, 255, 0.9);
  border: 1px solid rgba(0, 0, 0, 0.08);
}
.theme-transparent .line {
  color: #fff;
  text-shadow: 0 1px 3px rgba(0, 0, 0, 0.9);
  background: transparent;
  border: none;
  padding: 2px 0;
}
/* Role accents: a subtle left border in the role's conventional colour so a mod/VIP/sub/broadcaster
   message reads at a glance, without fighting the theme's own line background. */
.line.role-broadcaster {
  border-left: 3px solid #e91916;
}
.line.role-moderator {
  border-left: 3px solid #00ad03;
}
.line.role-vip {
  border-left: 3px solid #e005b9;
}
.line.role-subscriber {
  border-left: 3px solid #9146ff;
}
.line.cheer {
  border-left: 3px solid #f2b21c;
  background: color-mix(in srgb, #f2b21c 12%, transparent);
}
.role-badge {
  font-size: 0.65em;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.02em;
  padding: 1px 5px;
  border-radius: 4px;
  color: #fff;
  flex: none;
}
.role-badge-broadcaster {
  background: #e91916;
}
.role-badge-moderator {
  background: #00ad03;
}
.role-badge-vip {
  background: #e005b9;
}
.role-badge-subscriber {
  background: #9146ff;
}
.line-cheer-bits {
  font-size: 0.75em;
  font-weight: 700;
  color: #f2b21c;
  background: color-mix(in srgb, #f2b21c 20%, transparent);
  border-radius: 4px;
  padding: 0 5px;
}
.platform {
  font-size: 0.65em;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.02em;
  padding: 1px 5px;
  border-radius: 4px;
  color: #fff;
  flex: none;
  opacity: 0.9;
}
.platform-kick {
  background: #53fc18;
  color: #0a0a0a;
}
.platform-youtube {
  background: #ff0000;
}
/* Reply preview: a quoted line above the head row showing who/what this message replies to. */
.reply-preview {
  display: block;
  font-size: 0.8em;
  opacity: 0.7;
  margin-bottom: 2px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.reply-arrow {
  margin-right: 3px;
}
.reply-user {
  font-weight: 700;
  margin-right: 4px;
}
.head {
  /* Block-level (not inline-flex) so the head row and the message body below it are separate lines —
     the originally reported bug: username and message text were glued onto one run. */
  display: flex;
  align-items: center;
  gap: 4px;
  margin-bottom: 2px;
}
.avatar {
  width: 1.25em;
  height: 1.25em;
  border-radius: 50%;
  object-fit: cover;
  flex: none;
}
.badge {
  width: 1.125em;
  height: 1.125em;
  flex: none;
}
.name {
  font-weight: 700;
  color: var(--accent, #9146ff);
  /* Long display names truncate with an ellipsis instead of stretching/overflowing the head row. */
  display: inline-block;
  max-width: 12em;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  vertical-align: bottom;
}
/* A coloured header bar (7TV paint or Twitch name colour) sets the text colour itself, black or white by
   contrast, so the name and pronouns inherit it instead of taking the accent colour. */
.head-colored {
  padding: 2px 8px;
  border-radius: 6px;
}
.head-colored .name,
.head-colored .pron {
  color: inherit;
  -webkit-text-fill-color: currentColor;
}
.head-colored .pron {
  opacity: 1;
}
.pron {
  font-size: 12px;
  opacity: 0.75;
  font-family: ui-monospace, monospace;
}
.time {
  font-size: 0.72em;
  opacity: 0.6;
  font-variant-numeric: tabular-nums;
  margin-right: 2px;
}
.body {
  display: block;
}
.emote {
  /* Relative to the configurable font size, not a fixed pixel value, so raising font size for readability
     doesn't leave emotes visually undersized. */
  height: 1.5em;
  width: auto;
  vertical-align: middle;
  margin: 0 1px;
}
/* Link/track card. Inline-flex so it sits in the message body like any other fragment rather than breaking
   the line, and capped in width so a long title cannot blow out the chat column. */
.link-card {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  max-width: 100%;
  padding: 4px 8px;
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.08);
  text-decoration: none;
  vertical-align: middle;
}

.link-card-art {
  width: 40px;
  height: 40px;
  border-radius: 4px;
  object-fit: cover;
  flex: none;
}

.link-card-text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.link-card-title,
.link-card-sub {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.link-card-sub {
  opacity: 0.7;
  font-size: 0.85em;
}

/* Minimal functional styling for the remaining body fragment types — the visual design is themed elsewhere. */
.cheermote {
  height: 1.5em;
  width: auto;
  vertical-align: middle;
  margin: 0 1px;
}
.cheer-bits {
  font-weight: 700;
}
.chat-gif {
  /* Content, not a glyph — big enough to actually read, capped so one GIF can't blow out the chat column. */
  display: block;
  max-width: 100%;
  max-height: 8em;
  width: auto;
  border-radius: 6px;
  margin: 4px 0 0;
}
.mention {
  /* A highlighted chip so a mention stands out even when the mentioned user has no known chat colour
     (previously plain bold text, easy to miss). */
  font-weight: 700;
  /* The chip tints with the mentioned user's own colour, never the channel accent; a user with no colour keeps the
     inherited text colour, exactly like their own name in a chat line. */
  background: color-mix(in srgb, var(--mention-color, currentColor) 22%, transparent);
  border-radius: 4px;
  padding: 0 4px;
}
.link {
  color: var(--accent, #9146ff);
  text-decoration: underline;
}
/* Rich HTML fragment: keep it inline and tame runaway media so a message can't blow out the chat column. */
.frag-html {
  display: inline;
}
/* An emote the server stitched into the html (class="emote") stays emote-sized. */
.frag-html :deep(img.emote) {
  height: 1.5em;
  width: auto;
}
/* The sender's own image keeps the size they wrote (width/height attribute or inline css, both capped by the
   server); these caps only keep it inside the chat column. !important because inline css would win otherwise. */
.frag-html :deep(img),
.frag-html :deep(video) {
  max-height: 300px !important;
  object-fit: contain;
  vertical-align: middle;
}
.frag-html :deep(*) {
  max-width: 100% !important;
}
/* Arrival animation: new lines fade + slide in instead of popping in instantly; departing lines (overflow past
   maxMessages) fade + slide out the same way; remaining lines glide to their new position (TransitionGroup's
   move class). The `fadeAfterMs` fade-OUT-in-place (`.faded`) is a separate, orthogonal effect layered on top. */
.chat-line-enter-active,
.chat-line-leave-active {
  transition: opacity 0.25s ease, transform 0.25s ease;
}
.chat-line-enter-from {
  opacity: 0;
  transform: translateY(10px);
}
.chat-line-leave-to {
  opacity: 0;
  transform: translateY(-6px);
}
.chat-line-leave-active {
  position: absolute;
}
.chat-line-move {
  transition: transform 0.25s ease;
}
</style>
