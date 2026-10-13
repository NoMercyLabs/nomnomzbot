<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One overlay in the list: its icon, name, whether it is on and attached, and a Fails mark.
// The bot returns only whether a browser source is open, not how many, so the line says so in words.
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'

import type { Widget } from '@/api/widgets'
import StatusBadge from '@/components/layout/StatusBadge.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'

import { overlayKind, type OverlayKind } from './overlayKind'

const props = defineProps<{ widget: Widget; selected: boolean }>()
defineEmits<{ pick: [] }>()

const { t } = useI18n()

const kind: ComputedRef<OverlayKind> = computed<OverlayKind>(() => overlayKind(props.widget.name))
const detail: ComputedRef<string> = computed<string>(() => {
  if (!props.widget.isEnabled) return t('dashboard.overlays.list.off')
  return t(
    props.widget.isAttached
      ? 'dashboard.overlays.list.attached'
      : 'dashboard.overlays.list.notAttached',
  )
})
</script>

<template>
  <div data-overlay-row class="px-1 py-0.5">
    <SummaryRow
      pick
      :selected="selected"
      :aria-pressed="selected"
      :title="widget.name"
      :detail="detail"
      @click="$emit('pick')"
    >
      <template #leading>
        <span
          class="flex size-9 flex-none items-center justify-center rounded-md bg-muted text-muted-foreground"
        >
          <component :is="kind.icon" class="size-4" aria-hidden="true" />
        </span>
      </template>
      <template #end>
        <StatusBadge
          v-if="widget.lastRuntimeError"
          tone="bad"
          :label="$t('dashboard.overlays.list.fails')"
        />
      </template>
    </SummaryRow>
  </div>
</template>
