<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Edits the bet limits, odds, payout, cooldown, who can play and plays per stream of one mini-game. An
// emptied limit field means "no limit" and travels as null. The category is read-only: a gambling game
// keeps the server's cooldown floor (shown from the API) whatever the owner types. Save is the only
// primary action.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { ApiError } from '@/api/errors';
import { useSaveGameConfig, type GameConfig } from '@/api/games';
import { gameStandings, type GameStanding } from '@/api/vocabulary';
import DurationInput from '@/components/layout/DurationInput.vue';
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCan } from '@/composables/useCan';
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
const { can } = useCan();
const save = useSaveGameConfig(channelId);

const values = ref<Record<Field, number | string>>({
  minBet: '',
  maxBet: '',
  houseEdgePercent: '',
  winChancePercent: '',
  payoutMultiplier: '',
  maxPlaysPerStream: '',
});

const cooldown = ref(0);
const permission = ref<GameStanding>('Everyone');

const floorSeconds = computed(() => Number(props.config?.cooldownFloorSeconds ?? 0));

const categoryKey = computed(
  () => `dashboard.games.configs.category.${props.config?.category.toLowerCase()}`,
);

const canWrite = computed(() => can('economy:games:write'));

const failure = computed(() => {
  const error = save.error.value;

  if (error === null)
    return null;

  return error instanceof ApiError
    ? t(error.messageKey)
    : t('dashboard.games.configs.saveFailed');
});

function standingOf(word: string): GameStanding {
  return (
    gameStandings.find((standing) => standing.toLowerCase() === word.toLowerCase()) ?? 'Everyone'
  );
}

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

    cooldown.value = toNumber(config.cooldownSeconds) ?? 0;
    permission.value = standingOf(config.permission);
  },
  { immediate: true },
);

function submit(): void {
  if (props.config === null) return;

  const next: GameConfig = { ...props.config };

  for (const field of fields) {
    next[field] = toNumber(values.value[field]);
  }

  next.cooldownSeconds = Number.isFinite(cooldown.value)
    ? cooldown.value
    : 0;

  next.permission = permission.value;

  save.mutate(next, {
    onSuccess: () => {
      open.value = false;
    },
  });
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
          {{ t("dashboard.games.configs.editTitle", { name }) }}
        </DialogTitle>
        <DialogDescription class="text-sm text-muted-foreground">
          {{ t("dashboard.games.configs.editHint") }}
        </DialogDescription>
      </div>

      <div class="grid min-h-0 flex-1 gap-4 overflow-y-auto px-6 pb-4 sm:grid-cols-2">
        <div v-for="field in fields" :key="field" class="grid gap-1.5">
          <Label :for="`game-${field}`">{{ t(`dashboard.games.configs.fields.${field}`) }}</Label>
          <Input
            :id="`game-${field}`"
            v-model="values[field]"
            :data-game-field="field"
            :disabled="!canWrite"
            type="number"
            min="0"
            step="any"
          />
        </div>

        <div class="grid gap-1.5">
          <Label for="game-cooldownSeconds">
            {{ t("dashboard.games.configs.fields.cooldownSeconds") }}
          </Label>
          <DurationInput
            id="game-cooldownSeconds"
            v-model="cooldown"
            data-game-field="cooldownSeconds"
            :disabled="!canWrite"
          />
        </div>

        <div class="grid gap-1.5">
          <Label for="game-permission">{{ t("dashboard.games.configs.fields.permission") }}</Label>
          <Select v-model="permission" :disabled="!canWrite">
            <SelectTrigger id="game-permission" data-game-permission class="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="standing in gameStandings" :key="standing" :value="standing">
                {{ t(`dashboard.games.configs.standings.${standing}`) }}
              </SelectItem>
            </SelectContent>
          </Select>
        </div>

        <p class="text-sm text-muted-foreground sm:col-span-2" data-game-category>
          {{ t("dashboard.games.configs.category.line", { category: t(categoryKey) }) }}
        </p>

        <p
          v-if="floorSeconds > 0"
          class="text-sm text-muted-foreground sm:col-span-2"
          data-game-cooldown-floor
        >
          {{ t("dashboard.games.configs.floorStays", { seconds: floorSeconds }) }}
        </p>

        <p v-if="failure !== null" class="text-sm text-destructive sm:col-span-2" role="alert">
          {{ failure }}
        </p>
      </div>

      <DialogFooter v-if="canWrite" class="flex-none border-t border-border px-6 py-4">
        <Button variant="outline" @click="open = false">
          {{ t("dashboard.games.configs.cancel") }}
        </Button>
        <Button data-save-game-config :disabled="save.isPending.value" @click="submit">
          {{ t("dashboard.games.configs.save") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
