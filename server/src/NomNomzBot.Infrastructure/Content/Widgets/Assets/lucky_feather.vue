<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// This marketplace bundle has no settings schema, so onSettings hands it Record<string, unknown>.

// Chest-steal overlay for the "Lucky Feather" preset (marketplace bundle, not a FirstPartyWidgetCatalogue
// entry). The feather steal/expiry pipeline pushes two event types this widget listens for directly:
//   - "steal": { previousHolder: Holder | null, newHolder: Holder }
//   - "hide":  {} (or no payload) — the feather went into hiding (auto-hide expiry fired)
// A Holder is { id, displayName, avatarUrl, paint? } — `paint` is the SAME flattened 7TV ChatPaintDto shape
// chat_box.vue already renders (backgroundImage/color/textShadow/isImageOnly), present ONLY when the holder
// wears a paint (absent — not null, not {} — for a viewer wearing none; see ScriptHostBridge.GetUser).
interface ChatPaint {
  backgroundImage: string | null
  color: string | null
  textShadow: string | null
  isImageOnly: boolean
}

interface Holder {
  id: string
  displayName: string
  avatarUrl: string
  paint: ChatPaint | null
}

interface FeatherConfig {
  idleText: string       // shown when nobody currently holds the feather
  stolenTemplate: string // {thief} {victim}; empty = default copy
  bannerDurationMs: number
  accentColor: string
}

const cfg = reactive<FeatherConfig>({
  idleText: 'The feather is hidden…',
  stolenTemplate: '',
  bannerDurationMs: 5000,
  accentColor: '#f4b942',
})

const holder = ref<Holder | null>(null)
const bannerVisible = ref<boolean>(false)
const bannerText = ref<string>('')
let bannerTimer: number | undefined

// A paint's colour/gradient renders as the NAME's own style, exactly like chat_box.vue's paintNameStyle —
// background-clip: text for a gradient/image paint, a flat colour otherwise, falling back to the theme
// default when the holder wears none.
function paintNameStyle(p: ChatPaint | null): Record<string, string> {
  if (!p) return {}
  const style: Record<string, string> = {}
  if (p.backgroundImage) {
    style['background-image'] = p.backgroundImage
    style['background-clip'] = 'text'
    style['-webkit-background-clip'] = 'text'
    style.color = 'transparent'
    // Same defect chat_box.vue hit: an image paint is a 384x128 texture, and without an explicit
    // size the browser clips the unscaled top-left corner to the name box, which reads as one flat
    // colour. Proven on the rendered overlay; no payload-level test can see it.
    style['background-size'] = 'cover'
    style['background-position'] = 'center'
    style['background-repeat'] = 'no-repeat'
  } else if (p.color) {
    style.color = p.color
  }
  if (p.textShadow) style['text-shadow'] = p.textShadow
  return style
}

const holderNameStyle = computed<Record<string, string>>(() => paintNameStyle(holder.value?.paint || null))

function announce(previous: Holder | null, next: Holder): void {
  const victim = previous ? previous.displayName : 'no one'
  bannerText.value = cfg.stolenTemplate
    ? cfg.stolenTemplate.replace(/\{thief\}/g, next.displayName).replace(/\{victim\}/g, victim)
    : (previous ? `${next.displayName} stole the Feather from ${victim}!` : `${next.displayName} found the Feather!`)
  bannerVisible.value = true
  if (bannerTimer) window.clearTimeout(bannerTimer)
  bannerTimer = window.setTimeout(() => { bannerVisible.value = false }, Math.max(1000, cfg.bannerDurationMs))
}

// The "steal" and "hide" events are not in the typed event map, so their payloads arrive as `unknown`;
// a holder is read field by field from this loose shape, which also accepts the snake_case spellings.
interface RawPaint {
  backgroundImage?: unknown
  color?: unknown
  textShadow?: unknown
  isImageOnly?: unknown
}
interface RawHolder {
  id?: unknown
  displayName?: unknown
  display_name?: unknown
  avatarUrl?: unknown
  image_url?: unknown
  paint?: RawPaint | null
}
interface StealPayload {
  newHolder?: unknown
  previousHolder?: unknown
}

function readHolder(value: unknown): Holder | null {
  if (!value || typeof value !== 'object') return null
  const raw: RawHolder = value as RawHolder
  if (!raw.id) return null
  const rawPaint: RawPaint | null | undefined = raw.paint
  const paint: ChatPaint | null = rawPaint && typeof rawPaint === 'object' ? {
    backgroundImage: typeof rawPaint.backgroundImage === 'string' ? rawPaint.backgroundImage : null,
    color: typeof rawPaint.color === 'string' ? rawPaint.color : null,
    textShadow: typeof rawPaint.textShadow === 'string' ? rawPaint.textShadow : null,
    isImageOnly: !!rawPaint.isImageOnly,
  } : null
  return {
    id: String(raw.id),
    displayName: String(raw.displayName || raw.display_name || 'Someone'),
    avatarUrl: typeof raw.avatarUrl === 'string' ? raw.avatarUrl : (typeof raw.image_url === 'string' ? raw.image_url : ''),
    paint,
  }
}

// A replayed steal (seeded on join) only restores the holder card: the steal already happened, so no banner.
function onSteal(data: unknown, _type?: string, meta?: NnzEventMeta): void {
  const d: StealPayload = (data && typeof data === 'object' ? data : {}) as StealPayload
  const next = readHolder(d.newHolder)
  if (!next) return
  holder.value = next
  if (meta && meta.replay) return
  announce(readHolder(d.previousHolder), next)
}

function onHide(): void {
  holder.value = null
  bannerVisible.value = false
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.idleText === 'string') cfg.idleText = s.idleText
    if (typeof s.stolenTemplate === 'string') cfg.stolenTemplate = s.stolenTemplate
    if (isFinite(Number(s.bannerDurationMs)) && Number(s.bannerDurationMs) > 0) cfg.bannerDurationMs = Number(s.bannerDurationMs)
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  NomNomz.on('steal', onSteal)
  NomNomz.on('hide', onHide)
})

onUnmounted(() => {
  if (bannerTimer) window.clearTimeout(bannerTimer)
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('steal', onSteal)
  NomNomz.off('hide', onHide)
})
</script>

<template>
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file only places the pills. -->
  <div class="nnz-widget nnz-feather">
    <div class="nnz-pill nnz-tilt banner" :class="{ show: bannerVisible }">
      <span class="nnz-face"></span>
      <span class="nnz-title">{{ bannerText }}</span>
    </div>
    <div v-if="holder" class="nnz-pill holder-card">
      <img v-if="holder.avatarUrl" class="nnz-avatar avatar" :src="holder.avatarUrl" alt="">
      <div v-else class="badge">&#129413;</div>
      <span class="name" :class="{ 'name-painted': holder.paint && (holder.paint.backgroundImage || holder.paint.color) }" :style="holderNameStyle">{{ holder.displayName }}</span>
      <span class="nnz-sub holds">holds the Feather</span>
    </div>
    <div v-else class="nnz-pill nnz-sub idle">{{ cfg.idleText }}</div>
  </div>
</template>

<style scoped>
.nnz-feather {
  position: fixed;
  bottom: 5%;
  left: 50%;
  transform: translateX(-50%);
  pointer-events: none;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
}
.banner {
  opacity: 0;
  translate: 0 8px;
  transition: opacity var(--nnz-out) ease, translate var(--nnz-in) var(--nnz-ease);
}
.banner.show {
  opacity: 1;
  translate: 0 0;
}
.banner .nnz-title {
  font-size: 20px;
}
.avatar {
  width: 32px;
  height: 32px;
}
.badge {
  font-size: 20px;
  line-height: 1;
}
.name {
  font-weight: 800;
}
.holds {
  font-size: 13px;
}
.idle {
  font-size: 13px;
}
</style>
