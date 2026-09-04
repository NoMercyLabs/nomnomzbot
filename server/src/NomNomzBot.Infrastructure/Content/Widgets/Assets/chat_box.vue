<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

// Renders the decorated chat DTO ("ChatMessage" — the camelCase DashboardChatMessageDto shape the
// ChatMessageBroadcastHandler pushes to overlays: fragments with resolved emote urls, badges, colour,
// avatar, pronouns). Idle until messages arrive.
interface ChatBoxConfig {
  theme: string          // 'dark' | 'light' | 'transparent'
  maxMessages: number
  fadeAfterMs: number    // 0 = never fade
  showBadges: boolean
  showEmotes: boolean
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

// Clamp a chat color's lightness so it stays readable against a solid theme background — a dark color on the
// dark theme, or a light color on the light theme, is otherwise illegible (W·§2). The transparent theme keeps
// the color as-authored: its `.line` already applies a dark drop-shadow that helps any color read against an
// arbitrary OBS scene, and there is no fixed background to reason a minimum contrast against.
function contrastColor(hex: string, theme: string): string {
  if (theme !== 'dark' && theme !== 'light') return hex
  const h: string = hex.replace('#', '')
  const full: string = h.length === 3 ? h.split('').map((c) => c + c).join('') : h
  const r: number = parseInt(full.slice(0, 2), 16) / 255
  const g: number = parseInt(full.slice(2, 4), 16) / 255
  const b: number = parseInt(full.slice(4, 6), 16) / 255
  const max: number = Math.max(r, g, b)
  const min: number = Math.min(r, g, b)
  let hDeg = 0
  const l: number = (max + min) / 2
  const d: number = max - min
  const s: number = d === 0 ? 0 : d / (1 - Math.abs(2 * l - 1))
  if (d !== 0) {
    if (max === r) hDeg = ((g - b) / d) % 6
    else if (max === g) hDeg = (b - r) / d + 2
    else hDeg = (r - g) / d + 4
    hDeg *= 60
    if (hDeg < 0) hDeg += 360
  }
  const targetL: number = theme === 'dark' ? Math.max(l, 0.55) : Math.min(l, 0.4)
  if (targetL === l) return hex
  const c: number = (1 - Math.abs(2 * targetL - 1)) * s
  const x: number = c * (1 - Math.abs(((hDeg / 60) % 2) - 1))
  const m: number = targetL - c / 2
  let [r2, g2, b2]: number[] =
    hDeg < 60 ? [c, x, 0] : hDeg < 120 ? [x, c, 0] : hDeg < 180 ? [0, c, x] :
    hDeg < 240 ? [0, x, c] : hDeg < 300 ? [x, 0, c] : [c, 0, x]
  const toHex = (v: number) => Math.round((v + m) * 255).toString(16).padStart(2, '0')
  return `#${toHex(r2)}${toHex(g2)}${toHex(b2)}`
}

const rootStyle = computed<Record<string, string>>(() => {
  const style: Record<string, string> = { '--accent': cfg.accentColor, 'font-size': cfg.fontSize + 'px' }
  if (cfg.fontFamily) style['font-family'] = cfg.fontFamily
  return style
})

// An explicit background hex overrides the theme's line background (inline styles beat the theme class).
const lineStyle = computed<Record<string, string>>(() =>
  hexColor(cfg.background) ? { background: hexToRgba(cfg.background, cfg.backgroundOpacity) } : {},
)

function clockLabel(iso: any): string {
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

interface ChatLine {
  id: string
  // The RAW chat message id, unlike `id` which is suffixed with a sequence to stay unique when the same
  // message id arrives twice. Enrichment arrives keyed on this one.
  sourceId: string
  // Set when the bot later learned what this line meant — a song request resolving to a real track. When
  // present it REPLACES the body: the point is not to show "!sr <query>" and a card, it is to show the track.
  card: LinkCard | null
  name: string
  color: string
  pronouns: string
  avatarUrl: string
  badgeUrls: string[]
  fragments: any[]
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

// A paint's colour/gradient renders as the NAME's own style — not the line's — the same way 7TV's own
// extension paints only the name, never the whole chat bubble. A gradient needs background-clip: text (and a
// transparent fill colour to show it through); a flat colour just sets color. Image-only paints carry no
// usable colour (SevenTvPaintMapper flags them but resolves no url here), so they fall through to the theme's
// default name colour rather than rendering blank.
function paintNameStyle(p: ChatPaint | null): Record<string, string> {
  if (!p) return {}
  const style: Record<string, string> = {}
  if (p.backgroundImage) {
    style['background-image'] = p.backgroundImage
    style['background-clip'] = 'text'
    style['-webkit-background-clip'] = 'text'
    style.color = 'transparent'
  } else if (p.color) {
    style.color = p.color
  }
  if (p.textShadow) style['text-shadow'] = p.textShadow
  return style
}

const PROVIDER_LABEL: Record<string, string> = { twitch: 'Twitch', kick: 'Kick', youtube: 'YouTube' }

// Highest-priority role wins the line accent when a user carries more than one — broadcaster and
// moderator/VIP/subscriber are not mutually exclusive on Twitch (a mod can also be a subscriber).
function resolveRole(m: any): string {
  if (m.isBroadcaster) return 'broadcaster'
  if (m.isModerator) return 'moderator'
  if (m.isVip) return 'vip'
  if (m.isSubscriber) return 'subscriber'
  return ''
}

const lines = ref<ChatLine[]>([])
let seq = 0
const fadeTimers: number[] = []

function hexColor(c: any): string {
  return (typeof c === 'string' && /^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/.test(c.trim())) ? c.trim() : ''
}

function firstUrl(urls: any, keys: string[]): string {
  if (!urls) return ''
  for (let i = 0; i < keys.length; i++) if (urls[keys[i]]) return urls[keys[i]]
  return ''
}

function emoteUrl(fr: any): string {
  return firstUrl(fr && fr.emote && fr.emote.urls, ['2', '1', '3'])
}

function cheermoteUrl(fr: any): string {
  return firstUrl(fr && fr.cheermote && fr.cheermote.urls, ['2', '1', '3'])
}

// Twitch's native chat GIF (GIPHY-backed, Tier 2+ subscriber feature): the fragment already carries a
// directly-fetchable url — no separate resolve step, unlike media-share's clip/video lookups.
function gifUrl(fr: any): string {
  return (fr && fr.gif && typeof fr.gif.url === 'string') ? fr.gif.url : ''
}

// Mention/cheermote may carry a #RRGGBB accent; guard it before binding to style so bad data can't inject CSS.
function fragColor(c: any): Record<string, string> {
  const hex: string = hexColor(c)
  return hex ? { color: hex } : {}
}

// A line already on screen turned out to mean something richer — a song request resolving to a real track.
// Matched on the RAW message id; an id we never rendered (the line already scrolled off, or this overlay
// started after it) simply finds nothing and is ignored.
function onEnriched(e: any): void {
  const id: string = e && e.messageId ? String(e.messageId) : ''
  if (!id) return

  const line: ChatLine | undefined = lines.value.find((l: ChatLine) => l.sourceId === id)
  if (!line) return

  line.card = {
    linkUrl: e.linkUrl || undefined,
    title: e.title || undefined,
    description: e.description || undefined,
    imageUrl: e.imageUrl || undefined,
    provider: e.provider || undefined,
  }
}

function onChat(m: any): void {
  if (!m || typeof m !== 'object') return
  const text: string = m.message || ''
  if (cfg.hideCommands && (m.isCommand || text.charAt(0) === '!')) return
  const login: string = (m.username || '').toLowerCase()
  if (cfg.hideBots && KNOWN_BOTS.indexOf(login) !== -1) return

  const line: ChatLine = {
    id: (m.id || '') + '-' + (++seq),
    sourceId: m.id || '',
    card: null,
    name: m.displayName || m.username || 'Someone',
    color: hexColor(m.color),
    pronouns: m.pronouns || '',
    avatarUrl: typeof m.avatarUrl === 'string' ? m.avatarUrl : '',
    badgeUrls: cfg.showBadges
      ? (m.badges || []).map((b: any) => firstUrl(b.urls, ['2', '1', '4'])).filter((u: string) => !!u)
      : [],
    fragments: m.fragments || [],
    message: text,
    time: clockLabel(m.timestamp),
    faded: false,
    role: resolveRole(m),
    isCheer: !!m.isCheer,
    bitsAmount: isFinite(Number(m.bitsAmount)) ? Number(m.bitsAmount) : 0,
    replyUserName: typeof m.replyParentUserName === 'string' ? m.replyParentUserName : '',
    replyMessageBody: typeof m.replyParentMessageBody === 'string' ? m.replyParentMessageBody : '',
    provider: typeof m.provider === 'string' ? m.provider.toLowerCase() : 'twitch',
    paint: m.paint && typeof m.paint === 'object' ? {
      backgroundImage: typeof m.paint.backgroundImage === 'string' ? m.paint.backgroundImage : null,
      color: typeof m.paint.color === 'string' ? m.paint.color : null,
      textShadow: typeof m.paint.textShadow === 'string' ? m.paint.textShadow : null,
      isImageOnly: !!m.paint.isImageOnly,
    } : null,
  }
  const next: ChatLine[] = lines.value.concat([line])
  while (next.length > Math.max(1, cfg.maxMessages)) next.shift()
  lines.value = next

  if (cfg.fadeAfterMs > 0) {
    fadeTimers.push(window.setTimeout(() => { line.faded = true }, cfg.fadeAfterMs))
  }
}

onMounted(() => {
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.theme === 'string' && s.theme) cfg.theme = s.theme
    if (isFinite(Number(s.maxMessages)) && Number(s.maxMessages) > 0) cfg.maxMessages = Number(s.maxMessages)
    if (isFinite(Number(s.fadeAfterMs)) && Number(s.fadeAfterMs) >= 0) cfg.fadeAfterMs = Number(s.fadeAfterMs)
    if (typeof s.showBadges === 'boolean') cfg.showBadges = s.showBadges
    if (typeof s.showEmotes === 'boolean') cfg.showEmotes = s.showEmotes
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
  nnz.on('ChatMessage', onChat)
  nnz.on('ChatMessageEnriched', onEnriched)
})

onUnmounted(() => {
  fadeTimers.forEach((t: number) => window.clearTimeout(t))
  if (!nnz) return
  nnz.off('ChatMessage', onChat)
  nnz.off('ChatMessageEnriched', onEnriched)
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
      <span class="head">
        <span v-if="cfg.showTimestamps && l.time" class="time">{{ l.time }}</span>
        <span v-if="l.provider !== 'twitch'" class="platform" :class="'platform-' + l.provider">{{ PROVIDER_LABEL[l.provider] || l.provider }}</span>
        <img v-if="l.avatarUrl" class="avatar" :src="l.avatarUrl" alt="">
        <img v-for="(b, i) in l.badgeUrls" :key="i" class="badge" :src="b" alt="">
        <span v-if="l.role" class="role-badge" :class="'role-badge-' + l.role">{{ l.role }}</span>
        <span
          class="name"
          :class="{ 'name-painted': l.paint && (l.paint.backgroundImage || l.paint.color) }"
          :style="l.paint ? paintNameStyle(l.paint) : { color: l.color ? contrastColor(l.color, cfg.theme) : '' }"
        >{{ l.name }}</span>
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
            <span v-else-if="fr.type === 'mention' && fr.mention" class="mention" :style="fragColor(fr.mention.color)">{{ '@' + (fr.mention.displayName || fr.mention.username || '') }}</span>
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
/* A 7TV paint owns the name's colour outright — background-clip: text needs an opaque background box to clip
   against, which inline-block already gives .name for its ellipsis truncation. */
.name-painted {
  -webkit-text-fill-color: transparent;
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
  background: color-mix(in srgb, var(--accent, #9146ff) 22%, transparent);
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
.frag-html :deep(img) {
  max-height: 1.6em;
  width: auto;
  vertical-align: middle;
}
.frag-html :deep(*) {
  max-width: 100%;
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
