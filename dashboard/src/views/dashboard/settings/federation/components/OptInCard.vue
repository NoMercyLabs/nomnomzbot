<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One channel-wide yes/no that the broadcaster gives to the network: a card with the bot's stored value and a
// switch that carries the permission check. The wrappers pass the query, the save and their own wording.
import type { UseQueryReturnType } from '@tanstack/vue-query';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { usePanelState } from '@/composables/usePanelState';
import { errorText } from '@/lib/errors';

const props = defineProps<{
  scope: string;
  action: string;
  optIn: UseQueryReturnType<{ accepts: boolean }, Error>;
  save: (accepts: boolean) => Promise<void>;
  pending: boolean;
}>();

const { t } = useI18n();
const state = usePanelState(props.optIn);
const failure = ref<string | undefined>(undefined);

const accepts = computed({
  get: () => props.optIn.data.value?.accepts ?? false,
  set: (on) => void change(on),
});

async function change(on: boolean): Promise<void> {
  failure.value = undefined;

  try {
    await props.save(on);
  } catch (error) {
    failure.value = errorText(error, t);
  }
}
</script>

<template>
  <PanelCard
    :title="$t(`${scope}.title`)"
    :description="$t(`${scope}.description`)"
    :state="state"
    :error-text="$t(`${scope}.loadFailed`)"
    @retry="optIn.refetch()"
  >
    <RowGroup>
      <div class="flex min-h-11 flex-col gap-2 rounded-[8px] px-3 py-2 md:flex-row md:flex-wrap md:items-center md:gap-3">
        <div class="min-w-0 md:flex-1">
          <span class="block text-sm font-medium wrap-break-word">{{ $t(`${scope}.label`) }}</span>
          <span class="block text-sm text-muted-foreground">{{ $t(`${scope}.hint`) }}</span>
        </div>
        <label data-labeled-switch class="flex w-fit items-center gap-2 text-sm font-medium">
          {{ $t(`${scope}.switch`) }}
          <GatedSwitch
            v-model="accepts"
            :action="action"
            :aria-label="$t(`${scope}.switch`)"
            :disabled="pending"
          />
        </label>
      </div>
    </RowGroup>
    <p v-if="failure" role="alert" class="pt-3 text-sm text-destructive">{{ failure }}</p>
  </PanelCard>
</template>
