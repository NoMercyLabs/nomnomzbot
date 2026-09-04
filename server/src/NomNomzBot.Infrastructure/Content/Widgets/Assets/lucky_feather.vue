<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

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

function readHolder(raw: any): Holder | null {
  if (!raw || typeof raw !== 'object' || !raw.id) return null
  const paint = raw.paint && typeof raw.paint === 'object' ? {
    backgroundImage: typeof raw.paint.backgroundImage === 'string' ? raw.paint.backgroundImage : null,
    color: typeof raw.paint.color === 'string' ? raw.paint.color : null,
    textShadow: typeof raw.paint.textShadow === 'string' ? raw.paint.textShadow : null,
    isImageOnly: !!raw.paint.isImageOnly,
  } : null
  return {
    id: String(raw.id),
    displayName: String(raw.displayName || raw.display_name || 'Someone'),
    avatarUrl: typeof raw.avatarUrl === 'string' ? raw.avatarUrl : (typeof raw.image_url === 'string' ? raw.image_url : ''),
    paint,
  }
}

function onSteal(data: any): void {
  const d: any = data || {}
  const next = readHolder(d.newHolder)
  if (!next) return
  const previous = readHolder(d.previousHolder)
  holder.value = next
  announce(previous, next)
}

function onHide(): void {
  holder.value = null
  bannerVisible.value = false
}

onMounted(() => {
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.idleText === 'string') cfg.idleText = s.idleText
    if (typeof s.stolenTemplate === 'string') cfg.stolenTemplate = s.stolenTemplate
    if (isFinite(Number(s.bannerDurationMs)) && Number(s.bannerDurationMs) > 0) cfg.bannerDurationMs = Number(s.bannerDurationMs)
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  nnz.on('steal', onSteal)
  nnz.on('hide', onHide)
})

onUnmounted(() => {
  if (bannerTimer) window.clearTimeout(bannerTimer)
  if (!nnz) return
  nnz.off('steal', onSteal)
  nnz.off('hide', onHide)
})
</script>

<template>
  <div class="nnz-feather" :style="{ '--accent': cfg.accentColor }">
    <div class="banner" :class="{ show: bannerVisible }">{{ bannerText }}</div>
    <div v-if="holder" class="holder-card">
      <img v-if="holder.avatarUrl" class="avatar" :src="holder.avatarUrl" alt="">
      <div v-else class="badge">&#129413;</div>
      <span class="name" :class="{ 'name-painted': holder.paint && (holder.paint.backgroundImage || holder.paint.color) }" :style="holderNameStyle">{{ holder.displayName }}</span>
      <span class="holds">holds the Feather</span>
    </div>
    <div v-else class="idle">{{ cfg.idleText }}</div>
  </div>
</template>

<style scoped>
.nnz-feather {
  position: fixed;
  bottom: 5%;
  left: 50%;
  transform: translateX(-50%);
  pointer-events: none;
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  text-align: center;
}
.banner {
  margin-bottom: 8px;
  padding: 8px 20px;
  border-radius: 10px;
  color: #fff;
  font-weight: 800;
  background: rgba(12, 12, 18, 0.86);
  border: 2px solid var(--accent, #f4b942);
  opacity: 0;
  transform: translateY(8px);
  transition: opacity 0.3s ease, transform 0.3s cubic-bezier(0.22, 1, 0.36, 1);
}
.banner.show {
  opacity: 1;
  transform: translateY(0);
}
.holder-card {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 6px 16px;
  border-radius: 999px;
  background: rgba(12, 12, 18, 0.7);
  border: 2px solid var(--accent, #f4b942);
  color: #fff;
}
.avatar {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  object-fit: cover;
  border: 2px solid var(--accent, #f4b942);
}
.badge {
  font-size: 20px;
  line-height: 1;
}
.name {
  font-weight: 800;
}
.holds {
  opacity: 0.75;
  font-size: 13px;
}
.idle {
  padding: 6px 16px;
  border-radius: 999px;
  background: rgba(12, 12, 18, 0.45);
  color: rgba(255, 255, 255, 0.7);
  font-size: 13px;
  font-style: italic;
}
</style>
