<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One kind of data a channel can share with, or accept from, other bot instances: its name, what it does, and
// two independent switches. Accept takes data in, share sends it out. The bot stores one way per kind, so the
// pair maps to it: neither is off, both is both. Nothing leaves or enters until a switch is on.
import { computed } from 'vue';

import { type FederationDirection, type FederationOptInType } from '@/api/federation';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';

export type FederationChoice = 'off' | FederationDirection;

const props = defineProps<{
  type: FederationOptInType;
  choice: FederationChoice;
  busy: boolean;
}>();

const emit = defineEmits<{ choose: [choice: FederationChoice] }>();

const base = computed(() => `dashboard.settings.federation.type.${props.type}`);

function choiceOf(accept: boolean, share: boolean): FederationChoice {
  if (accept && share)
    return 'both';

  if (accept)
    return 'accept';

  return share
    ? 'share'
    : 'off';
}

const accepts = computed({
  get: () => props.choice === 'accept' || props.choice === 'both',
  set: (on) => emit('choose', choiceOf(on, shares.value)),
});

const shares = computed({
  get: () => props.choice === 'share' || props.choice === 'both',
  set: (on) => emit('choose', choiceOf(accepts.value, on)),
});
</script>

<template>
  <div :data-opt-in="type" class="flex min-h-11 flex-col gap-2 rounded-[8px] px-3 py-2 md:flex-row md:flex-wrap md:items-center md:gap-3">
    <div class="min-w-0 flex-1">
      <span class="block text-sm font-medium wrap-break-word">{{ $t(`${base}.title`) }}</span>
      <span class="block text-sm text-muted-foreground">{{ $t(`${base}.description`) }}</span>
    </div>
    <div data-choice-pair class="flex items-center gap-4">
      <LabeledSwitch
        v-model="accepts"
        action="federation:optin:write"
        :label="$t('dashboard.settings.federation.choice.accept')"
        :disabled="busy"
      />
      <LabeledSwitch
        v-model="shares"
        action="federation:optin:write"
        :label="$t('dashboard.settings.federation.choice.share')"
        :disabled="busy"
      />
    </div>
  </div>
</template>
