<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Load a model, or fire one hotkey of the loaded model. Trigger is the one primary action; Load is outline.
import { ref } from 'vue';

import { useVtsControl, type VtsModelInventory } from '@/api/vts';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
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
