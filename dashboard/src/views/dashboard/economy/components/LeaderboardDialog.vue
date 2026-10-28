<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Add or change a leaderboard: what it ranks, over which set (the whole channel or one jar), for which
// period, how many places it shows, and whether viewers can see it.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useSaveLeaderboardConfig, useSavingsJars, type LeaderboardConfig } from '@/api/economy';
import FormDialog from '@/components/layout/FormDialog.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

const metrics = ['balance', 'earned', 'spent'] as const;
const scopes = ['channel', 'jar'] as const;
const periods = ['alltime', 'daily', 'weekly', 'monthly'] as const;

const props = defineProps<{ config: LeaderboardConfig | null }>();
const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const save = useSaveLeaderboardConfig(channelId);
const jars = useSavingsJars(channelId);

const metric = ref<string>('balance');
const scope = ref<string>('channel');
const period = ref<string>('alltime');
const topN = ref(10);
const isPublic = ref(true);
const jarId = ref('');
const error = ref<string | undefined>(undefined);

watch(
  open,
  (isOpen: boolean): void => {
    if (!isOpen) return;

    metric.value = props.config?.metric ?? 'balance';
    scope.value = props.config?.scope ?? 'channel';
    period.value = props.config?.period ?? 'alltime';
    topN.value = Number(props.config?.topN ?? 10);
    isPublic.value = props.config?.isPublic ?? true;
    jarId.value = props.config?.jarId ?? '';
    error.value = undefined;
  },
  { immediate: true },
);

async function submit(): Promise<void> {
  if (scope.value === 'jar' && jarId.value === '') {
    error.value = t('dashboard.economy.leaderboards.dialog.jarRequired');

    return;
  }

  error.value = undefined;

  try {
    await save.mutateAsync({
      id: props.config?.id ?? null,
      metric: metric.value,
      scope: scope.value,
      period: period.value,
      isPublic: isPublic.value,
      topN: topN.value,
      jarId: scope.value === 'jar'
        ? jarId.value
        : null,
    });
  } catch (failure) {
    error.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');

    return;
  }

  open.value = false;
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    :title="
      config
        ? $t('dashboard.economy.leaderboards.dialog.editTitle')
        : $t('dashboard.economy.leaderboards.dialog.newTitle')
    "
    :description="$t('dashboard.economy.leaderboards.dialog.description')"
    :action="$t('dashboard.economy.leaderboards.dialog.save')"
    :pending="save.isPending.value"
    :error="error"
    @submit="submit"
  >
    <div class="grid gap-4 sm:grid-cols-2">
      <Field>
        <FieldLabel>{{ $t('dashboard.economy.leaderboards.dialog.metric') }}</FieldLabel>
        <Select v-model="metric">
          <SelectTrigger
            :aria-label="$t('dashboard.economy.leaderboards.dialog.metric')"
            class="w-full"
          >
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="item in metrics" :key="item" :value="item">
              {{ $t(`dashboard.economy.leaderboards.metrics.${item}`) }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <FieldLabel>{{ $t('dashboard.economy.leaderboards.dialog.period') }}</FieldLabel>
        <Select v-model="period">
          <SelectTrigger
            :aria-label="$t('dashboard.economy.leaderboards.dialog.period')"
            class="w-full"
          >
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="item in periods" :key="item" :value="item">
              {{ $t(`dashboard.economy.leaderboards.periods.${item}`) }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <FieldLabel>{{ $t('dashboard.economy.leaderboards.dialog.scope') }}</FieldLabel>
        <Select v-model="scope">
          <SelectTrigger
            :aria-label="$t('dashboard.economy.leaderboards.dialog.scope')"
            class="w-full"
          >
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="item in scopes" :key="item" :value="item">
              {{ $t(`dashboard.economy.leaderboards.scopes.${item}`) }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <FieldLabel for="leaderboard-top">{{
          $t('dashboard.economy.leaderboards.dialog.topN')
        }}</FieldLabel>
        <Input
          id="leaderboard-top"
          v-model.number="topN"
          type="number"
          min="1"
          max="100"
          step="1"
          :aria-label="$t('dashboard.economy.leaderboards.dialog.topN')"
        />
      </Field>
    </div>
    <Field v-if="scope === 'jar'">
      <FieldLabel>{{ $t('dashboard.economy.leaderboards.dialog.jar') }}</FieldLabel>
      <Select v-model="jarId">
        <SelectTrigger :aria-label="$t('dashboard.economy.leaderboards.dialog.jar')" class="w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="item in jars.data.value ?? []" :key="item.id" :value="item.id">
            {{ item.name }}
          </SelectItem>
        </SelectContent>
      </Select>
    </Field>
    <SettingRow
      v-model="isPublic"
      :title="$t('dashboard.economy.leaderboards.dialog.public')"
      :description="$t('dashboard.economy.leaderboards.dialog.publicHelp')"
    />
  </FormDialog>
</template>
