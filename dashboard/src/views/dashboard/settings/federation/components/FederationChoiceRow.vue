<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
  <div :data-opt-in="type" class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2">
    <div class="min-w-0 flex-1">
      <span class="block text-sm font-medium wrap-break-word">{{ $t(`${base}.title`) }}</span>
      <span class="block text-sm text-muted-foreground">{{ $t(`${base}.description`) }}</span>
    </div>
    <div class="flex items-center gap-4">
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
