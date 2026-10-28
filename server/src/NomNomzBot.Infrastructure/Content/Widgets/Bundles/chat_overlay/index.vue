<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { reactive, ref, onMounted, onUnmounted } from 'vue'
import ChatMessage from './components/ChatMessage.vue'
import type { ChatLine, Features, LineLook } from './types'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its event payloads
// (NnzDashboardChatMessageDto, NnzMessageDeletedDto, ...) come from the widget nnz.d.ts the editor loads.

// Port of the legacy "Twitch Chat Overlay" (01K0QVBQYEGAQKAX1CYTK1S61M): animated chat with a
// per-user colour banner, avatar bubble, staged enter sequence (banner → avatar → bubble → content),
// a one-shot shine sweep, badges, pronouns, emotes and link previews. Driven by "ChatMessage".
interface ChatOverlayConfig {
  maxMessages: number
  chatAnimationDuration: number
  chatShowDuration: number
  defaultColor: string
  width: number
  height: number
  hideCommands: boolean
  animations: {
    sequenceTiming: { bannerDelay: number; avatarDelay: number; bubbleDelay: number; contentDelay: number }
    durations: { banner: string; avatar: string; bubble: string; content: string; shine: string }
    easings: { default: string; entrance: string; exit: string }
  }
  features: Features
}

const cfg = reactive<ChatOverlayConfig>({
  maxMessages: 4,
  chatAnimationDuration: 0.75,
  chatShowDuration: 30,
  defaultColor: '#f72264',
  width: 520,
  height: 800,
  // Matches the legacy bot: a chat command line (e.g. "!sr <song>") never shows up itself — only its
  // reply does, so a viewer sees "Added <track> to the queue" in the spot where the raw command would
  // have gone, never the command text plus the reply as two separate lines.
  hideCommands: true,
  animations: {
    sequenceTiming: { bannerDelay: 0, avatarDelay: 0.3, bubbleDelay: 0.6, contentDelay: 0.9 },
    durations: { banner: '0.5s', avatar: '0.3s', bubble: '0.4s', content: '0.3s', shine: '12s' },
    easings: { default: 'ease-out', entrance: 'ease-in-out', exit: 'ease-in-out' },
  },
  features: {
    userBadges: true,
    userPronouns: true,
    userColors: true,
    lightColorDetection: true,
    shineEffect: true,
    ogPreviews: true,
    emoteSupport: true,
  },
})

const lines = ref<ChatLine[]>([])
let seq = 0
const timers: number[] = []

function later(fn: () => void, ms: number): void {
  timers.push(window.setTimeout(fn, ms))
}

function hexColor(c: unknown): string {
  return (typeof c === 'string' && /^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/.test(c.trim())) ? c.trim() : ''
}

function firstUrl(urls: Record<string, string> | null | undefined, keys: string[]): string {
  if (!urls) return ''
  for (const key of keys) {
    const url: string | undefined = urls[key]
    if (url) return url
  }
  return ''
}

function badgeUrls(badges: NnzChatBadgeDto[] | null | undefined): string[] {
  return (badges || []).map((b: NnzChatBadgeDto) => firstUrl(b && b.urls, ['2', '1', '4'])).filter((u: string) => !!u)
}

function isEmoteOnly(fragments: NnzChatFragmentDto[]): boolean {
  if (!fragments.length) return false
  return fragments.every((f: NnzChatFragmentDto) => f.type === 'emote' || (f.type === 'text' && String(f.text || '').trim() === ''))
}

function isRecord(v: unknown): v is Record<string, unknown> {
  return !!v && typeof v === 'object' && !Array.isArray(v)
}

function positive(v: unknown): number | null {
  const n: number = Number(v)
  return isFinite(n) && n > 0 ? n : null
}

// Same convention as the built-in Chat Box widget: the server-decorated payload carries `isCommand`
// when it knows for certain, but the raw '!' prefix is the reliable fallback for any command the
// server didn't tag (a viewer's own unrecognized "!whatever" still reads as a command visually).
function isCommandMessage(m: NnzDashboardChatMessageDto): boolean {
  // The bot's own overlay-only line is the answer to a command, never the command itself, so hideCommands keeps it.
  if (m.messageType === 'overlay_line') return false
  if (m.isCommand === true) return true
  const text: string = String(m.message || '').trim()
  return text.charAt(0) === '!'
}

// Who a viewer is, remembered from any line they send — including the "!sr ..." line that hideCommands is
// about to discard. A song-request card is drawn as the requester's OWN bubble, and the enrichment event
// carries only their name, so without this the bubble has no avatar, no colour and no badges. Capturing it
// above the command filter is what makes the requester's own command line the source of their identity.
interface Identity {
  color: string
  pronouns: string
  avatarUrl: string
  badgeUrls: string[]
  paint: NnzChatPaintDto | null
  userId: string
}

const identities = new Map<string, Identity>()

function paintOf(m: NnzDashboardChatMessageDto): NnzChatPaintDto | null {
  return m.paint && typeof m.paint === 'object' ? m.paint : null
}

function rememberIdentity(m: NnzDashboardChatMessageDto): void {
  const login: string = String(m.username || m.displayName || '').toLowerCase()
  if (!login) return

  // Bounded like the line list: a channel's chatters are unbounded, this map must not be.
  if (identities.size >= 200 && !identities.has(login)) {
    const oldest: string | undefined = identities.keys().next().value
    if (oldest) identities.delete(oldest)
  }

  identities.set(login, {
    color: hexColor(m.color),
    pronouns: m.pronouns || '',
    avatarUrl: m.avatarUrl || '',
    badgeUrls: badgeUrls(m.badges),
    paint: paintOf(m),
    userId: typeof m.userId === 'string' ? m.userId : '',
  })
}

// The design tokens a channel can pick for a bot overlay line, as the dark-theme colors the dashboard shows. A token
// this widget does not know is ignored, never drawn as a guess.
const HIGHLIGHT_COLORS: Record<string, string> = {
  primary: 'oklch(0.692953 0.114282 309.045)',
  success: 'oklch(0.795095 0.234563 145.534)',
  warning: 'oklch(0.8 0.16 75)',
  destructive: 'oklch(0.710627 0.166148 22.216)',
}

function lookOf(value: NnzOverlayLineDto | null | undefined): LineLook | null {
  if (!isRecord(value)) return null
  const token: string = typeof value.highlight === 'string' && value.highlight in HIGHLIGHT_COLORS ? value.highlight : ''
  return {
    requestedBy: typeof value.requestedBy === 'string' ? value.requestedBy.trim() : '',
    highlight: token,
    color: HIGHLIGHT_COLORS[token] ?? '',
    large: value.size === 'large',
    durationSeconds: positive(value.durationSeconds) ?? 0,
  }
}

function onChat(m: NnzDashboardChatMessageDto): void {
  if (!m || typeof m !== 'object') return
  rememberIdentity(m)
  if (cfg.hideCommands && isCommandMessage(m)) return

  const fragments: NnzChatFragmentDto[] = Array.isArray(m.fragments) ? m.fragments : []
  const line: ChatLine = {
    id: (m.id || '') + '-' + (++seq),
    sourceId: String(m.id || ''),
    userId: typeof m.userId === 'string' ? m.userId : '',
    name: m.displayName || m.username || 'Someone',
    color: hexColor(m.color),
    pronouns: m.pronouns || '',
    avatarUrl: m.avatarUrl || '',
    badgeUrls: cfg.features.userBadges ? badgeUrls(m.badges) : [],
    fragments,
    message: m.message || '',
    emoteOnly: isEmoteOnly(fragments),
    state: 'entering',
    card: null,
    paint: paintOf(m),
    look: lookOf(m.overlayLine),
  }

  pushLine(line)
}

// The shared queue + enter/leave sequence. Both a chat line and a song card become a bubble the same
// way, so the animation timing can never drift between them.
function pushLine(line: ChatLine): void {
  // Legacy queue behavior: push out the oldest with its leave animation when full.
  if (lines.value.length >= Math.max(1, cfg.maxMessages)) {
    const oldest: ChatLine | undefined = lines.value[0]
    if (oldest) {
      oldest.state = 'leaving'
      later(() => { lines.value = lines.value.filter((x) => x.id !== oldest.id) }, cfg.chatAnimationDuration * 1000)
    }
  }
  lines.value = lines.value.concat([line])

  // State flips must go through the REACTIVE array entry — `line` is the raw object; the ref wraps
  // it in a proxy on insert, and mutating the raw target never notifies Vue.
  const setState = (id: string, state: string): void => {
    const entry = lines.value.find((x) => x.id === id)
    if (entry) entry.state = state
  }

  later(() => { setState(line.id, 'active') }, cfg.chatAnimationDuration * 1000)
  later(() => {
    setState(line.id, 'leaving')
    later(() => { lines.value = lines.value.filter((x) => x.id !== line.id) }, cfg.chatAnimationDuration * 1000)
  }, (line.look?.durationSeconds || cfg.chatShowDuration) * 1000)
}

// A song request the bot has finished processing. It arrives resolved (title, artist, artwork, provider)
// and names the viewer who asked, because this overlay hides command lines: the "!sr ..." line was never
// rendered, so there is no earlier bubble to read a name off or to swap a body into. Draw the requester's
// own bubble with the track card as its content instead.
function onEnriched(e: NnzChatMessageEnrichedWidgetPayload): void {
  if (!e || typeof e !== 'object') return
  if (!e.title) return

  const login: string = String(e.userLogin || e.userDisplayName || '').toLowerCase()
  const who: Identity | undefined = identities.get(login)

  pushLine({
    id: (e.messageId || 'card') + '-' + (++seq),
    sourceId: String(e.messageId || ''),
    userId: who?.userId ?? '',
    name: e.userDisplayName || e.userLogin || 'Someone',
    color: who?.color ?? '',
    pronouns: who?.pronouns ?? '',
    avatarUrl: who?.avatarUrl ?? '',
    badgeUrls: cfg.features.userBadges ? (who?.badgeUrls ?? []) : [],
    fragments: [],
    message: '',
    emoteOnly: false,
    state: 'entering',
    paint: who?.paint ?? null,
    look: null,
    card: {
      title: e.title,
      description: e.description || '',
      imageUrl: e.imageUrl || '',
      linkUrl: e.linkUrl || '',
      provider: e.provider || '',
    },
  })
}

const FEATURE_KEYS: (keyof Features)[] = [
  'userBadges',
  'userPronouns',
  'userColors',
  'lightColorDetection',
  'shineEffect',
  'ogPreviews',
  'emoteSupport',
]

function mergeSettings(s: NnzWidgetSettings): void {
  if (!isRecord(s)) return
  cfg.maxMessages = positive(s.maxMessages) ?? cfg.maxMessages
  cfg.chatAnimationDuration = positive(s.chatAnimationDuration) ?? cfg.chatAnimationDuration
  cfg.chatShowDuration = positive(s.chatShowDuration) ?? cfg.chatShowDuration
  if (typeof s.defaultColor === 'string' && hexColor(s.defaultColor)) cfg.defaultColor = s.defaultColor
  cfg.width = positive(s.width) ?? cfg.width
  cfg.height = positive(s.height) ?? cfg.height
  if (typeof s.hideCommands === 'boolean') cfg.hideCommands = s.hideCommands
  const animations: unknown = s.animations
  if (isRecord(animations)) {
    if (isRecord(animations.sequenceTiming)) Object.assign(cfg.animations.sequenceTiming, animations.sequenceTiming)
    if (isRecord(animations.durations)) Object.assign(cfg.animations.durations, animations.durations)
    if (isRecord(animations.easings)) Object.assign(cfg.animations.easings, animations.easings)
  }
  const features: unknown = s.features
  if (isRecord(features)) {
    FEATURE_KEYS.forEach((key: keyof Features) => {
      const value: unknown = features[key]
      if (typeof value === 'boolean') cfg.features[key] = value
    })
  }
}

// A moderator or AutoMod deleted ONE message: drop the line(s) drawn from that raw message id.
function onMessageDeleted(d: NnzMessageDeletedDto): void {
  const id: string = d && d.messageId ? String(d.messageId) : ''
  if (!id) return
  lines.value = lines.value.filter((l: ChatLine) => l.sourceId !== id)
}

// A whole-channel "Clear chat": every rendered line goes.
function onChatCleared(): void {
  lines.value = []
}

// A per-chatter purge: every line from that one person goes.
function onUserMessagesCleared(d: NnzUserMessagesClearedDto): void {
  const uid: string = d && d.targetUserId ? String(d.targetUserId) : ''
  if (!uid) return
  lines.value = lines.value.filter((l: ChatLine) => l.userId !== uid)
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings(mergeSettings)
    .on('ChatMessage', onChat)
    .on('ChatMessageEnriched', onEnriched)
    .on('MessageDeleted', onMessageDeleted)
    .on('ChatCleared', onChatCleared)
    .on('UserMessagesCleared', onUserMessagesCleared)
})

onUnmounted(() => {
  timers.forEach((t: number) => window.clearTimeout(t))
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('ChatMessage', onChat)
    .off('ChatMessageEnriched', onEnriched)
    .off('MessageDeleted', onMessageDeleted)
    .off('ChatCleared', onChatCleared)
    .off('UserMessagesCleared', onUserMessagesCleared)
})
</script>

<template>
  <div
    class="nnz-stoney-chat"
    :style="{
      '--accent': cfg.defaultColor,
      '--anim-dur': cfg.chatAnimationDuration + 's',
      '--banner-dur': cfg.animations.durations.banner,
      '--avatar-dur': cfg.animations.durations.avatar,
      '--bubble-dur': cfg.animations.durations.bubble,
      '--content-dur': cfg.animations.durations.content,
      '--shine-dur': cfg.animations.durations.shine,
      '--banner-delay': cfg.animations.sequenceTiming.bannerDelay + 's',
      '--avatar-delay': cfg.animations.sequenceTiming.avatarDelay + 's',
      '--bubble-delay': cfg.animations.sequenceTiming.bubbleDelay + 's',
      '--content-delay': cfg.animations.sequenceTiming.contentDelay + 's',
      '--ease-default': cfg.animations.easings.default,
      '--ease-entrance': cfg.animations.easings.entrance,
      width: 'calc(' + cfg.width + 'px - 32px)',
      height: 'calc(' + cfg.height + 'px - 32px)',
    }"
  >
    <ChatMessage v-for="l in lines" :key="l.id" :line="l" :default-color="cfg.defaultColor" :features="cfg.features" />
  </div>
</template>

<style scoped>
.nnz-stoney-chat *,
.nnz-stoney-chat *::before,
.nnz-stoney-chat *::after {
  box-sizing: border-box;
}
.nnz-stoney-chat {
  position: fixed;
  top: 0;
  left: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 8px 4px 0 16px;
  margin: 16px;
  overflow: hidden;
  pointer-events: none;
  box-sizing: border-box;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
}
</style>
