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
  <div class="nnz-chatbox" :class="'theme-' + cfg.theme" :style="rootStyle">
    <div v-for="l in lines" :key="l.id" class="line" :class="{ faded: l.faded }" :style="lineStyle">
      <span class="head">
        <span v-if="cfg.showTimestamps && l.time" class="time">{{ l.time }}</span>
        <img v-for="(b, i) in l.badgeUrls" :key="i" class="badge" :src="b" alt="">
        <span class="name" :style="l.color ? { color: l.color } : {}">{{ l.name }}</span>
        <span v-if="l.pronouns" class="pron">({{ l.pronouns }})</span>
      </span>
      <span class="body">
        <template v-if="l.fragments.length">
          <template v-for="(fr, i) in l.fragments" :key="i">
            <img v-if="fr.type === 'emote' && cfg.showEmotes && fr.emote" class="emote" :src="(fr.emote.urls && (fr.emote.urls['2'] || fr.emote.urls['1'] || fr.emote.urls['3'])) || ''" :alt="fr.text">
            <span v-else>{{ fr.text || '' }}</span>
          </template>
        </template>
        <template v-else>{{ l.message }}</template>
      </span>
    </div>
  </div>
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
  display: inline-flex;
  align-items: center;
  gap: 4px;
  margin-right: 6px;
}
.badge {
  width: 18px;
  height: 18px;
}
.name {
  font-weight: 700;
  color: var(--accent, #9146ff);
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
.emote {
  height: 24px;
  width: auto;
  vertical-align: middle;
  margin: 0 1px;
}
</style>
