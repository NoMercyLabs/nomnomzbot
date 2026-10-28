<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Load a model, or fire one hotkey of the loaded model. Trigger is the one primary action; Load is outline.
import { ref } from 'vue';

import { useVtsControl, type VtsModelInventory } from '@/api/vts';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { toastVtsError } from '../vtsToast';

defineProps<{
  inventory: VtsModelInventory | null | undefined;
  panelState: PanelState;
  offline: boolean;
}>();

defineEmits<{ retry: [] }>();

const { channelId } = useCurrentChannel();
const { can } = useCan();
const control = useVtsControl(channelId);

const modelId = ref<string>('');
const hotkeyId = ref<string>('');

function loadModel(): void {
  control.mutate(
    { requestType: 'ModelLoadRequest', payload: { modelID: modelId.value } },
    { onError: toastVtsError },
  );
}

function trigger(): void {
  control.mutate(
    { requestType: 'HotkeyTriggerRequest', payload: { hotkeyID: hotkeyId.value } },
    { onError: toastVtsError },
  );
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.vts.hotkeys.title')"
    :state="offline ? 'empty' : panelState"
    :empty-text="$t('dashboard.vts.offline')"
    :error-text="$t('dashboard.vts.hotkeys.loadError')"
    @retry="$emit('retry')"
  >
    <div class="grid gap-3 p-3">
      <div class="grid gap-1 text-sm text-muted-foreground">
        {{ $t("dashboard.vts.hotkeys.model") }}
        <div class="flex gap-2">
          <Select v-model="modelId">
            <SelectTrigger
              data-vts-model
              :aria-label="$t('dashboard.vts.hotkeys.model')"
              class="w-full"
            >
              <SelectValue :placeholder="$t('dashboard.vts.hotkeys.pickModel')" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="model in inventory?.models" :key="model.id" :value="model.id">
                {{ model.name }}
              </SelectItem>
            </SelectContent>
          </Select>
          <Button
            v-if="can('vts:control')"
            data-vts-load-model
            type="button"
            variant="outline"
            :disabled="modelId === '' || control.isPending.value"
            @click="loadModel"
          >
            {{ $t("dashboard.vts.hotkeys.load") }}
          </Button>
        </div>
      </div>
      <div class="grid gap-1 text-sm text-muted-foreground">
        {{ $t("dashboard.vts.hotkeys.hotkey") }}
        <div class="flex gap-2">
          <Select v-model="hotkeyId">
            <SelectTrigger
              data-vts-hotkey
              :aria-label="$t('dashboard.vts.hotkeys.hotkey')"
              class="w-full"
            >
              <SelectValue :placeholder="$t('dashboard.vts.hotkeys.pickHotkey')" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="hotkey in inventory?.hotkeys" :key="hotkey.id" :value="hotkey.id">
                {{ hotkey.name }}
              </SelectItem>
            </SelectContent>
          </Select>
          <Button
            v-if="can('vts:control')"
            data-vts-trigger
            type="button"
            :disabled="hotkeyId === '' || control.isPending.value"
            @click="trigger"
          >
            {{ $t("dashboard.vts.hotkeys.trigger") }}
          </Button>
        </div>
      </div>
    </div>
  </PanelCard>
</template>
