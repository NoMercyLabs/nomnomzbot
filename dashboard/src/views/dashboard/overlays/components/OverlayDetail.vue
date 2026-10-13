<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The head of one overlay: name and badges, the On/Off switch, the one primary action (copy the
// browser source address), Health, what it reacts to, and the module facts.
// The address holds a secret token. Copy sends the full address to the clipboard; the page only ever shows it cut.
import { ArrowLeft, Check, Copy } from '@lucide/vue'
import { useTimeAgoIntl, useTimeoutFn } from '@vueuse/core'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { useSetWidgetEnabled, type Widget } from '@/api/widgets'
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue'
import PanelCard from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Switch } from '@/components/ui/switch'
import { useCurrentChannel } from '@/composables/useCurrentChannel'

import { maskedAddress, overlayKind, plainEvent, type OverlayKind } from './overlayKind'

const COPIED_MS = 2000

const props = defineProps<{ widget: Widget }>()
defineEmits<{ back: [] }>()

const { t, locale } = useI18n()
const { channelId } = useCurrentChannel()
const setEnabled = useSetWidgetEnabled(channelId)

const kind: ComputedRef<OverlayKind> = computed<OverlayKind>(() => overlayKind(props.widget.name))
const builtIn: ComputedRef<boolean> = computed<boolean>(() => props.widget.source === 'first_party')
const ago = useTimeAgoIntl(() => props.widget.lastRanAt ?? new Date(0), { locale: locale.value })

const copied: Ref<boolean> = ref(false)
const { start: resetCopied } = useTimeoutFn(
  () => {
    copied.value = false
  },
  COPIED_MS,
  { immediate: false },
)

async function copyAddress(): Promise<void> {
  if (!props.widget.overlayUrl) return
  await navigator.clipboard.writeText(props.widget.overlayUrl)
  copied.value = true
  resetCopied()
}

function toggle(isEnabled: boolean): void {
  setEnabled.mutate({ widgetId: props.widget.id, isEnabled })
}

const runTone: ComputedRef<StatusTone> = computed<StatusTone>(() =>
  props.widget.lastRuntimeError ? 'bad' : 'ok',
)
const runTitle: ComputedRef<string> = computed<string>(() =>
  t(
    props.widget.lastRuntimeError
      ? 'dashboard.overlays.detail.health.failed'
      : 'dashboard.overlays.detail.health.works',
  ),
)
const runNote: ComputedRef<string> = computed<string>(() => {
  if (props.widget.lastRuntimeError) return props.widget.lastRuntimeError
  if (!props.widget.lastRanAt) return t('dashboard.overlays.detail.health.neverRan')
  return t('dashboard.overlays.detail.health.worksRan', { ago: ago.value })
})

interface HealthLine {
  tone: StatusTone
  title: string
  note: string
}
const attachLine: ComputedRef<HealthLine> = computed<HealthLine>(() => {
  const base: string = 'dashboard.overlays.detail.health'
  if (!props.widget.isEnabled)
    return { tone: 'off', title: t(`${base}.switchedOff`), note: t(`${base}.switchedOffNote`) }
  if (props.widget.isAttached)
    return { tone: 'ok', title: t(`${base}.attached`), note: t(`${base}.attachedNote`) }
  return { tone: 'off', title: t(`${base}.detached`), note: t(`${base}.detachedNote`) }
})

const events: ComputedRef<string[]> = computed<string[]>(() => props.widget.eventSubscriptions)

const module: ComputedRef<KeyValueItem[]> = computed<KeyValueItem[]>(() => {
  const base: string = 'dashboard.overlays.detail.module'
  const address: string | null = props.widget.overlayUrl
    ? maskedAddress(props.widget.overlayUrl)
    : null
  return [
    { label: t(`${base}.what`), value: props.widget.description || t(`${base}.noDescription`) },
    { label: t(`${base}.framework`), value: props.widget.framework },
    { label: t(`${base}.triggers`), value: events.value.length },
    {
      label: t(`${base}.address`),
      value:
        address === null ? t(`${base}.noAddress`) : t(`${base}.addressNote`, { path: address }),
    },
  ]
})
</script>

<template>
  <div data-slot="overlay-detail" class="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto">
    <div class="flex flex-wrap items-center gap-3">
      <Button
        variant="ghost"
        size="icon-sm"
        class="md:hidden"
        :aria-label="$t('dashboard.overlays.back')"
        @click="$emit('back')"
      >
        <ArrowLeft />
      </Button>
      <span
        class="flex size-12 flex-none items-center justify-center rounded-lg bg-muted text-muted-foreground"
      >
        <component :is="kind.icon" class="size-5" aria-hidden="true" />
      </span>
      <div class="grid min-w-0 gap-1">
        <h2 class="truncate text-lg font-semibold">{{ widget.name }}</h2>
        <div class="flex flex-wrap items-center gap-2">
          <Badge variant="outline">{{ $t(`dashboard.overlays.kinds.${kind.key}`) }}</Badge>
          <Badge v-if="builtIn" variant="outline">{{
            $t('dashboard.overlays.detail.builtIn')
          }}</Badge>
          <Badge v-if="widget.isCustomized" variant="outline">{{
            $t('dashboard.overlays.detail.edited')
          }}</Badge>
          <span class="text-sm text-muted-foreground">{{ widget.framework }}</span>
        </div>
      </div>
      <div class="ml-auto flex flex-wrap items-center gap-3">
        <label class="flex items-center gap-2 text-sm text-muted-foreground">
          {{
            $t(widget.isEnabled ? 'dashboard.overlays.detail.on' : 'dashboard.overlays.detail.off')
          }}
          <Switch
            :model-value="widget.isEnabled"
            :aria-label="$t('dashboard.overlays.detail.switchLabel')"
            @update:model-value="toggle(Boolean($event))"
          />
        </label>
        <div class="grid justify-items-end gap-1">
          <Button :disabled="!widget.overlayUrl" @click="copyAddress">
            <component :is="copied ? Check : Copy" aria-hidden="true" />
            {{ $t(copied ? 'dashboard.overlays.detail.copied' : 'dashboard.overlays.detail.copy') }}
          </Button>
          <span v-if="!widget.overlayUrl" class="text-sm text-muted-foreground">{{
            $t('dashboard.overlays.detail.noAddress')
          }}</span>
        </div>
      </div>
    </div>

    <!-- The mockup's two columns (12-overlays.html:355-367): a grid child does not shrink, so no panel
         scrolls inside itself. -->
    <div class="grid flex-none items-start gap-4 xl:grid-cols-2">
      <div class="grid gap-4">
        <PanelCard tier="lead" pad :title="$t('dashboard.overlays.detail.health.title')">
          <div data-slot="overlay-health" class="grid gap-3 text-sm">
            <div class="grid gap-1">
              <StatusBadge :tone="runTone" :label="runTitle" class="font-medium" />
              <p class="text-muted-foreground wrap-break-word">{{ runNote }}</p>
            </div>
            <div class="grid gap-1">
              <StatusBadge :tone="attachLine.tone" :label="attachLine.title" class="font-medium" />
              <p class="text-muted-foreground">{{ attachLine.note }}</p>
            </div>
          </div>
        </PanelCard>

        <PanelCard
          :title="
            events.length === 1
              ? $t('dashboard.overlays.detail.events.titleOne')
              : $t('dashboard.overlays.detail.events.title', { count: events.length })
          "
          :state="events.length === 0 ? 'empty' : 'ready'"
          :empty-text="$t('dashboard.overlays.detail.events.none')"
        >
          <RowGroup>
            <SummaryRow
              v-for="key in events"
              :key="key"
              :data-event="key"
              :title="plainEvent(key)"
              :detail="plainEvent(key) === key ? undefined : key"
            />
          </RowGroup>
        </PanelCard>
      </div>

      <PanelCard tier="quiet" pad :title="$t('dashboard.overlays.detail.module.title')">
        <KeyValueList :items="module" />
      </PanelCard>
    </div>
  </div>
</template>
