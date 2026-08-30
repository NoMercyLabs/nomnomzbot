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

interface ChatLine {
  id: string
  name: string
  color: string
  pronouns: string
  avatarUrl: string
  badgeUrls: string[]
  fragments: any[]
  message: string
  time: string
  faded: boolean
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

// Mention/cheermote may carry a #RRGGBB accent; guard it before binding to style so bad data can't inject CSS.
function fragColor(c: any): Record<string, string> {
  const hex: string = hexColor(c)
  return hex ? { color: hex } : {}
}

function onChat(m: any): void {
  if (!m || typeof m !== 'object') return
  const text: string = m.message || ''
  if (cfg.hideCommands && (m.isCommand || text.charAt(0) === '!')) return
  const login: string = (m.username || '').toLowerCase()
  if (cfg.hideBots && KNOWN_BOTS.indexOf(login) !== -1) return

  const line: ChatLine = {
    id: (m.id || '') + '-' + (++seq),
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
})

onUnmounted(() => {
  fadeTimers.forEach((t: number) => window.clearTimeout(t))
  if (!nnz) return
  nnz.off('ChatMessage', onChat)
})
</script>

<template>
  <TransitionGroup tag="div" name="chat-line" class="nnz-chatbox" :class="'theme-' + cfg.theme" :style="rootStyle">
    <div v-for="l in lines" :key="l.id" class="line" :class="{ faded: l.faded }" :style="lineStyle">
      <span class="head">
        <span v-if="cfg.showTimestamps && l.time" class="time">{{ l.time }}</span>
        <img v-if="l.avatarUrl" class="avatar" :src="l.avatarUrl" alt="">
        <img v-for="(b, i) in l.badgeUrls" :key="i" class="badge" :src="b" alt="">
        <span class="name" :style="{ color: l.color ? contrastColor(l.color, cfg.theme) : '' }">{{ l.name }}</span>
        <span v-if="l.pronouns" class="pron">({{ l.pronouns }})</span>
      </span>
      <span class="body">
        <template v-if="l.fragments.length">
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
            <!-- @mention: a highlighted chip always, additionally tinted with the mentioned user's chat colour
                 when known — a mention must stand out even with no known colour. -->
            <span v-else-if="fr.type === 'mention' && fr.mention" class="mention" :style="fragColor(fr.mention.color)">{{ '@' + (fr.mention.displayName || fr.mention.username || '') }}</span>
            <!-- Shared link -->
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
