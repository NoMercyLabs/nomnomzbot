<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Edits the bet limits, odds, payout and plays per stream of one mini-game. An emptied field means "no
// limit" and travels as null. Save is the only primary action.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useSaveGameConfig, type GameConfig } from '@/api/games';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const props = defineProps<{ config: GameConfig | null; name: string }>();

const open = defineModel<boolean>('open', { required: true });

const fields = [
  'minBet',
  'maxBet',
  'houseEdgePercent',
  'winChancePercent',
  'payoutMultiplier',
  'maxPlaysPerStream',
] as const;

type Field = (typeof fields)[number];

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const save = useSaveGameConfig(channelId);

const values = ref<Record<Field, number | string>>({
  minBet: '',
  maxBet: '',
  houseEdgePercent: '',
  winChancePercent: '',
  payoutMultiplier: '',
  maxPlaysPerStream: '',
});

function text(value: number | string | null | undefined): string {
  return value === null || value === undefined
    ? ''
    : String(value);
}

// A number input may hand back a number or a string, so both are read as text first.
function toNumber(value: number | string): number | null {
  const trimmed = String(value).trim();

  return trimmed === ''
    ? null
    : Number(trimmed);
}

watch(
  () => [open.value, props.config] as const,
  ([isOpen, config]) => {
    if (!isOpen || config === null) return;

    for (const field of fields) {
      values.value[field] = text(config[field]);
    }
  },
  { immediate: true },
);

function submit(): void {
  if (props.config === null) return;

  const next: GameConfig = { ...props.config };

  for (const field of fields) {
    next[field] = toNumber(values.value[field]);
  }

  save.mutate(next, { onSuccess: () => {
    open.value = false;
  } });
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent
      :show-close-button="false"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 data-[size=default]:sm:max-w-120"
    >
      <div class="grid flex-none gap-1.5 px-6 pt-6 pb-4">
        <DialogTitle class="text-lg font-semibold">
          {{ t('dashboard.games.configs.editTitle', { name }) }}
        </DialogTitle>
        <DialogDescription class="text-sm text-muted-foreground">
          {{ t('dashboard.games.configs.editHint') }}
        </DialogDescription>
      </div>

      <div class="grid min-h-0 flex-1 gap-4 overflow-y-auto px-6 pb-4 sm:grid-cols-2">
        <div v-for="field in fields" :key="field" class="grid gap-1.5">
          <Label :for="`game-${field}`">{{ t(`dashboard.games.configs.fields.${field}`) }}</Label>
          <Input
            :id="`game-${field}`"
            v-model="values[field]"
            :data-game-field="field"
            type="number"
            min="0"
            step="any"
          />
        </div>
      </div>

      <DialogFooter class="flex-none border-t border-border px-6 py-4">
        <Button variant="outline" @click="open = false">
          {{ t('dashboard.games.configs.cancel') }}
        </Button>
        <Button data-save-game-config :disabled="save.isPending.value" @click="submit">
          {{ t('dashboard.games.configs.save') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
