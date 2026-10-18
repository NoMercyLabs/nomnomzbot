<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One kind of data a channel can share with, or accept from, other bot instances: its name, what it does, and
// one choice: off, accept, share or both. Off is the default; nothing leaves or enters until a choice is on.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  federationDirections,
  type FederationDirection,
  type FederationOptInType,
} from '@/api/federation';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';

export type FederationChoice = 'off' | FederationDirection;

const props = defineProps<{
  type: FederationOptInType;
  choice: FederationChoice;
  busy: boolean;
}>();

defineEmits<{ choose: [choice: FederationChoice] }>();

const { t } = useI18n();

const base = computed(() => `dashboard.settings.federation.type.${props.type}`);

const options = computed(() =>
  (['off', ...federationDirections] as const).map((value) => ({
    value,
    label: t(`dashboard.settings.federation.choice.${value}`),
  })));
</script>

<template>
  <div :data-opt-in="type" class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2">
    <div class="min-w-0 flex-1">
      <span class="block text-sm font-medium wrap-break-word">{{ $t(`${base}.title`) }}</span>
      <span class="block text-sm text-muted-foreground">{{ $t(`${base}.description`) }}</span>
    </div>
    <SegmentedControl
      :label="$t(`${base}.title`)"
      :options="options"
      :disabled="busy"
      :model-value="choice"
      @update:model-value="$emit('choose', $event)"
    />
  </div>
</template>
