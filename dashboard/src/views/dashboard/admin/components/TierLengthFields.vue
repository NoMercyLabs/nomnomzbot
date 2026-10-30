<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The tier picker and the length (whole months, or forever) of a gift or an invite (state: useTierLength).
// With no tier picked there is nothing to last, so the length waits for a tier.
import { computed } from 'vue';

import OptionSelect, { type Choice } from '@/components/layout/OptionSelect.vue';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Switch } from '@/components/ui/switch';

import { MAX_MONTHS, MIN_MONTHS } from './tierLength';

const tierId = defineModel<string>('tierId', { required: true });
const months = defineModel<string>('months', { required: true });
const forever = defineModel<boolean>('forever', { required: true });

const props = defineProps<{
  // The field ids start with it, so two forms on one page never share a label target.
  id: string;
  choices: readonly Choice[];
}>();

const noTier = computed<boolean>(() => tierId.value === '');
</script>

<template>
  <div class="grid gap-2">
    <Label :for="`${props.id}-tier`">{{ $t('dashboard.admin.billing.length.tier') }}</Label>
    <OptionSelect
      :id="`${props.id}-tier`"
      v-model="tierId"
      data-length-tier
      :options="choices"
      :placeholder="$t('dashboard.admin.billing.length.pickTier')"
    />
  </div>
  <div class="grid gap-2">
    <Label :for="`${props.id}-months`">{{ $t('dashboard.admin.billing.length.months') }}</Label>
    <div class="flex items-center gap-4">
      <Input :id="`${props.id}-months`"
             v-model="months"
             data-length-months
             type="number"
             class="w-28"
             :min="MIN_MONTHS"
             :max="MAX_MONTHS"
             :disabled="forever || noTier"
      />
      <div data-touch-target class="flex min-h-9 items-center gap-3">
        <Label :for="`${props.id}-forever`">{{ $t('dashboard.admin.billing.length.forever') }}</Label>
        <Switch :id="`${props.id}-forever`" v-model="forever" data-length-forever :disabled="noTier" />
      </div>
    </div>
  </div>
</template>
