<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One overlay in the list: its icon, name, whether it is on and attached, and a Fails mark.
// The bot returns only whether a browser source is open, not how many, so the line says so in words.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { Widget } from '@/api/widgets';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';

import ObsSetupAction from './ObsSetupAction.vue';
import { overlayHealth, type HealthDot } from './overlayHealth';
import { overlayKind, type OverlayKind } from './overlayKind';

const props = defineProps<{ widget: Widget; selected: boolean }>();

defineEmits<{ pick: [] }>();

const { t } = useI18n();

const kind = computed<OverlayKind>(() => overlayKind(props.widget.eventSubscriptions));
const detail = computed<string>(() => {
  if (!props.widget.isEnabled)
    return t('dashboard.overlays.list.off');

  return t(
    props.widget.isAttached
      ? 'dashboard.overlays.list.attached'
      : 'dashboard.overlays.list.notAttached',
  );
});

const dotState = computed<HealthDot>(() => overlayHealth(props.widget));
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
      <template v-if="kind.icon" #leading>
        <span
          data-overlay-icon
          class="flex size-9 flex-none items-center justify-center rounded-md bg-muted text-muted-foreground"
        >
          <component :is="kind.icon" class="size-4" aria-hidden="true" />
        </span>
      </template>
      <template #detail-lead>
        <span data-overlay-dot class="dot" :class="dotState" aria-hidden="true" />
      </template>
      <template #end>
        <StatusBadge
          v-if="widget.lastRuntimeError"
          tone="bad"
          :label="$t('dashboard.overlays.list.fails')"
        />
      </template>
      <template #trailing>
        <ObsSetupAction
          :title="widget.name"
          :setup="widget.obsSetup"
          :connected="widget.isAttached"
          :expected="widget.isEnabled"
          :disabled="!widget.overlayUrl"
          :resolve-address="async () => widget.overlayUrl"
        />
      </template>
    </SummaryRow>
  </div>
</template>
