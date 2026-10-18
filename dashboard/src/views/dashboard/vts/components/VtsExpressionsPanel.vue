<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Turn one expression of the loaded model on or off. Activate is the one primary action.
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
const expression = ref<string>('');

function set(active: boolean): void {
  control.mutate(
    {
      requestType: 'ExpressionActivationRequest',
      payload: { expressionFile: expression.value, active },
    },
    { onError: toastVtsError },
  );
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.vts.expressions.title')"
    :state="offline ? 'empty' : panelState"
    :empty-text="$t('dashboard.vts.offline')"
    :error-text="$t('dashboard.vts.expressions.loadError')"
    @retry="$emit('retry')"
  >
    <div class="grid gap-1 p-3 text-sm text-muted-foreground">
      {{ $t("dashboard.vts.expressions.expression") }}
      <div class="flex gap-2">
        <Select v-model="expression">
          <SelectTrigger
            data-vts-expression
            :aria-label="$t('dashboard.vts.expressions.expression')"
            class="w-full"
          >
            <SelectValue :placeholder="$t('dashboard.vts.expressions.pick')" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="file in inventory?.expressions" :key="file" :value="file">
              {{ file }}
            </SelectItem>
          </SelectContent>
        </Select>
        <Button
          v-if="can('vts:control')"
          data-vts-deactivate
          type="button"
          variant="outline"
          :disabled="expression === '' || control.isPending.value"
          @click="set(false)"
        >
          {{ $t("dashboard.vts.expressions.deactivate") }}
        </Button>
        <Button
          v-if="can('vts:control')"
          data-vts-activate
          type="button"
          :disabled="expression === '' || control.isPending.value"
          @click="set(true)"
        >
          {{ $t("dashboard.vts.expressions.activate") }}
        </Button>
      </div>
    </div>
  </PanelCard>
</template>
