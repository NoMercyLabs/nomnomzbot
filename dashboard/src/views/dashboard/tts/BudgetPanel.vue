<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useTtsConfig } from '@/api/tts';
import { useSaveTtsBudgets, useTtsOptions, type TtsBudgetChange } from '@/api/ttsRules';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const config = useTtsConfig(channelId);
const options = useTtsOptions(channelId);
const save = useSaveTtsBudgets(channelId);
const budgets = ref<TtsBudgetChange['providerBudgets']>([]);
const fallback = ref<string[]>([]);

watch(() => config.data.value, (stored) => {
  if (!stored) return;

  budgets.value = (stored.providerBudgets ?? []).map((budget) => ({
    provider: budget.provider,
    monthlyCharacters: budget.monthlyCharacters,
    shares: budget.shares.filter((share) => share.name !== 'default').map((share) => ({
      name: share.name, monthlyCharacters: share.monthlyCharacters,
    })),
  }));

  fallback.value = [...(stored.fallbackProviders ?? [])];
}, { immediate: true });

const availableProviders = computed(() => (options.data.value?.providers ?? [])
  .filter((provider) => !budgets.value.some((budget) => budget.provider === provider)));
const availableFallback = computed(() => (options.data.value?.providers ?? [])
  .filter((provider) => !fallback.value.includes(provider)));
const valid = computed(() => budgets.value.every((budget) =>
  Number.isInteger(budget.monthlyCharacters) && budget.monthlyCharacters > 0 &&
  budget.shares.every((share) => share.name.trim() !== '' && share.name !== 'default' &&
    Number.isInteger(share.monthlyCharacters) && share.monthlyCharacters > 0) &&
    new Set(budget.shares.map((share) => share.name)).size === budget.shares.length &&
      budget.shares.reduce((sum, share) => sum + share.monthlyCharacters, 0) <=
      budget.monthlyCharacters));

function defaultShare(budget: TtsBudgetChange['providerBudgets'][number]): number {
  return budget.monthlyCharacters -
    budget.shares.reduce((sum, share) => sum + share.monthlyCharacters, 0);
}

function providerUsage(provider: string) {
  return config.data.value?.providerBudgets?.find((row) => row.provider === provider);
}

function shareUsage(provider: string, share: string) {
  return providerUsage(provider)?.shares.find((row) => row.name === share);
}

function move(index: number, direction: number): void {
  const next = index + direction;

  if (next < 0 || next >= fallback.value.length) return;

  const [provider] = fallback.value.splice(index, 1);

  if (provider === undefined) return;

  fallback.value.splice(next, 0, provider);
}

function submit(): void {
  if (!valid.value) return;

  save.mutate({ providerBudgets: budgets.value, fallbackProviders: fallback.value }, {
    onSuccess: () => toast.success(t('dashboard.tts.budget.saved')),
    onError: () => toast.error(t('dashboard.tts.budget.saveFailed')),
  });
}
</script>

<template>
  <PanelCard pad :title="$t('dashboard.tts.budget.title')" :description="$t('dashboard.tts.budget.description')">
    <form class="flex flex-col gap-6" @submit.prevent="submit">
      <div v-for="(budget, index) in budgets" :key="budget.provider" class="flex flex-col gap-3 border-b pb-4">
        <div class="flex items-center justify-between gap-2">
          <h3 class="font-medium">{{ budget.provider }}</h3>
          <Button type="button" variant="ghost" @click="budgets.splice(index, 1)">
            {{ $t('dashboard.tts.budget.remove') }}
          </Button>
        </div>
        <Field>
          <FieldLabel :for="`budget-${budget.provider}`">{{ $t('dashboard.tts.budget.monthly') }}</FieldLabel>
          <Input :id="`budget-${budget.provider}`"
                 v-model.number="budget.monthlyCharacters"
                 type="number"
                 min="1"
                 class="w-40"
          />
          <FieldInfo>{{ $t('dashboard.tts.budget.usage', {
            used: providerUsage(budget.provider)?.used ?? 0,
            left: providerUsage(budget.provider)?.remaining ?? budget.monthlyCharacters,
          }) }}</FieldInfo>
        </Field>
        <div class="text-sm">{{ $t('dashboard.tts.budget.defaultShare', { count: defaultShare(budget) }) }}</div>
        <FieldInfo>{{ $t('dashboard.tts.budget.usage', {
          used: shareUsage(budget.provider, 'default')?.used ?? 0,
          left: shareUsage(budget.provider, 'default')?.remaining ?? defaultShare(budget),
        }) }}</FieldInfo>
        <div v-for="(share, shareIndex) in budget.shares" :key="shareIndex" class="flex flex-wrap items-end gap-2">
          <Field class="w-36">
            <FieldLabel :for="`share-${budget.provider}-${shareIndex}`">{{ $t('dashboard.tts.budget.shareName') }}</FieldLabel>
            <Input :id="`share-${budget.provider}-${shareIndex}`" v-model="share.name" />
          </Field>
          <Field class="w-36">
            <FieldLabel :for="`share-amount-${budget.provider}-${shareIndex}`">{{ $t('dashboard.tts.budget.monthly') }}</FieldLabel>
            <Input :id="`share-amount-${budget.provider}-${shareIndex}`" v-model.number="share.monthlyCharacters" type="number" min="1" />
          </Field>
          <FieldInfo>{{ $t('dashboard.tts.budget.usage', {
            used: shareUsage(budget.provider, share.name)?.used ?? 0,
            left: shareUsage(budget.provider, share.name)?.remaining ?? share.monthlyCharacters,
          }) }}</FieldInfo>
          <Button type="button" variant="ghost" @click="budget.shares.splice(shareIndex, 1)">
            {{ $t('dashboard.tts.budget.remove') }}
          </Button>
        </div>
        <Button type="button" variant="outline" class="self-start" @click="budget.shares.push({ name: '', monthlyCharacters: 1 })">
          {{ $t('dashboard.tts.budget.addShare') }}
        </Button>
      </div>
      <div class="flex flex-wrap gap-2">
        <Button v-for="provider in availableProviders"
                :key="provider"
                type="button"
                variant="outline"
                @click="budgets.push({ provider, monthlyCharacters: 0, shares: [] })"
        >
          {{ $t('dashboard.tts.budget.addProvider', { provider }) }}
        </Button>
      </div>
      <div class="flex flex-col gap-2">
        <h3 class="font-medium">{{ $t('dashboard.tts.budget.fallback') }}</h3>
        <FieldInfo>{{ $t('dashboard.tts.budget.fallbackHint') }}</FieldInfo>
        <div v-for="(provider, index) in fallback" :key="provider" class="flex items-center gap-2 text-sm">
          <span class="min-w-24">{{ $t('dashboard.tts.budget.position', {
            position: index + 1, provider,
          }) }}</span>
          <Button type="button" variant="outline" :disabled="index === 0" @click="move(index, -1)">
            {{ $t('dashboard.tts.budget.up') }}
          </Button>
          <Button type="button" variant="outline" :disabled="index === fallback.length - 1" @click="move(index, 1)">
            {{ $t('dashboard.tts.budget.down') }}
          </Button>
          <Button type="button" variant="ghost" @click="fallback.splice(index, 1)">
            {{ $t('dashboard.tts.budget.remove') }}
          </Button>
        </div>
        <div class="flex flex-wrap gap-2">
          <Button v-for="provider in availableFallback"
                  :key="provider"
                  type="button"
                  variant="outline"
                  @click="fallback.push(provider)"
          >
            {{ $t('dashboard.tts.budget.addFallback', { provider }) }}
          </Button>
        </div>
      </div>
      <Button type="submit" class="self-start" :disabled="!valid || save.isPending.value">
        {{ $t('dashboard.tts.budget.save') }}
      </Button>
    </form>
  </PanelCard>
</template>
