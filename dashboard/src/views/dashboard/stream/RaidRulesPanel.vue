<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// How this channel picks who to raid. Every point comes from these rules, per channel; a new channel starts neutral.
// The form edits a copy. Save is the one primary action and sends the whole set; the server refuses a set that
// breaks a limit and keeps the old one.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useRaidRules, useSaveRaidRules, type RaidRules } from '@/api/schedule';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Switch } from '@/components/ui/switch';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import Plus from '~icons/nnz/add';
import Cross from '~icons/nnz/cross';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const canEdit = computed(() => can('live-ops:raids:write'));
const query = useRaidRules(channelId);
const save = useSaveRaidRules(channelId);

const state = usePanelState(query);
const draft = ref<RaidRules | null>(null);

watch(
  () => query.data.value,
  (rules: RaidRules | null | undefined) => {
    const json = JSON.stringify(rules ?? null);

    draft.value = JSON.parse(json);
  },
  { immediate: true },
);

const dirty = computed(() => JSON.stringify(draft.value) !== JSON.stringify(query.data.value));

function words(text: string): string[] {
  return text
    .split(',')
    .map((word: string) => word.trim())
    .filter((word: string) => word !== '');
}

function addCategory(): void {
  draft.value?.preferredCategories.push({
    name: '',
    weight: 0,
    label: '',
  });
}

function addKeyword(): void {
  draft.value?.keywords.push({
    label: '',
    weight: 0,
    words: [],
    matchTags: true,
    matchTitle: true,
  });
}

function addBand(): void {
  draft.value?.viewerBands.push({
    min: 0,
    max: 0,
    weight: 0,
    label: '',
  });
}

function addStep(): void {
  draft.value?.recency.steps.push({
    withinDays: 0,
    weight: 0,
    note: '',
  });
}

function removeAt<T>(list: T[] | undefined, index: number): void {
  list?.splice(index, 1);
}

function submit(): void {
  if (draft.value) {
    save.mutate(draft.value);
  }
}
</script>

<template>
  <PanelCard
    :title="t('dashboard.stream.raids.title')"
    :description="t('dashboard.stream.raids.description')"
    tier="lead"
    :state="state"
    :error-text="t('dashboard.stream.raids.error')"
    @retry="query.refetch()"
  >
    <div v-if="draft" :inert="!canEdit" class="flex flex-col gap-6">
      <section class="flex flex-col gap-2">
        <span class="text-sm font-medium">{{ t('dashboard.stream.raids.preferredCategories') }}</span>
        <div v-for="(rule, index) in draft.preferredCategories" :key="index" class="flex items-center gap-2" data-category-rule>
          <Input v-model="rule.name" :aria-label="t('dashboard.stream.raids.name')" :placeholder="t('dashboard.stream.raids.name')" />
          <Input v-model.number="rule.weight" type="number" class="w-24" :aria-label="t('dashboard.stream.raids.weight')" />
          <Input v-model="rule.label" :aria-label="t('dashboard.stream.raids.label')" :placeholder="t('dashboard.stream.raids.label')" />
          <Button v-if="canEdit"
                  variant="ghost"
                  size="icon"
                  :aria-label="t('dashboard.stream.raids.remove')"
                  @click="removeAt(draft.preferredCategories, index)"
          >
            <Cross class="size-5" />
          </Button>
        </div>
        <Button v-if="canEdit"
                variant="outline"
                size="sm"
                class="w-fit"
                @click="addCategory"
        >
          <Plus class="size-5" />{{ t('dashboard.stream.raids.addCategory') }}
        </Button>
        <div class="grid gap-3 sm:grid-cols-2">
          <div class="grid gap-1.5">
            <Label for="other-weight">{{ t('dashboard.stream.raids.otherCategoryWeight') }}</Label>
            <Input id="other-weight" v-model.number="draft.otherCategoryWeight" type="number" />
          </div>
          <div class="grid gap-1.5">
            <Label for="other-label">{{ t('dashboard.stream.raids.otherCategoryLabel') }}</Label>
            <Input id="other-label" v-model="draft.otherCategoryLabel" />
          </div>
        </div>
      </section>

      <section class="flex flex-col gap-2">
        <span class="text-sm font-medium">{{ t('dashboard.stream.raids.keywords') }}</span>
        <div v-for="(rule, index) in draft.keywords" :key="index" class="flex flex-wrap items-center gap-2" data-keyword-rule>
          <Input v-model="rule.label" class="w-40" :aria-label="t('dashboard.stream.raids.label')" :placeholder="t('dashboard.stream.raids.label')" />
          <Input v-model.number="rule.weight" type="number" class="w-24" :aria-label="t('dashboard.stream.raids.weight')" />
          <Input
            :model-value="rule.words.join(', ')"
            class="min-w-48 flex-1"
            :aria-label="t('dashboard.stream.raids.words')"
            :placeholder="t('dashboard.stream.raids.words')"
            @update:model-value="rule.words = words(String($event))"
          />
          <label class="flex items-center gap-1.5 text-sm">
            <Switch v-model="rule.matchTags" />{{ t('dashboard.stream.raids.matchTags') }}
          </label>
          <label class="flex items-center gap-1.5 text-sm">
            <Switch v-model="rule.matchTitle" />{{ t('dashboard.stream.raids.matchTitle') }}
          </label>
          <Button v-if="canEdit"
                  variant="ghost"
                  size="icon"
                  :aria-label="t('dashboard.stream.raids.remove')"
                  @click="removeAt(draft.keywords, index)"
          >
            <Cross class="size-5" />
          </Button>
        </div>
        <Button v-if="canEdit"
                variant="outline"
                size="sm"
                class="w-fit"
                @click="addKeyword"
        >
          <Plus class="size-5" />{{ t('dashboard.stream.raids.addKeyword') }}
        </Button>
      </section>

      <section class="flex flex-col gap-2">
        <span class="text-sm font-medium">{{ t('dashboard.stream.raids.viewerBands') }}</span>
        <div v-for="(band, index) in draft.viewerBands" :key="index" class="flex items-center gap-2" data-viewer-band>
          <Input v-model.number="band.min" type="number" class="w-24" :aria-label="t('dashboard.stream.raids.min')" />
          <Input v-model.number="band.max" type="number" class="w-24" :aria-label="t('dashboard.stream.raids.max')" />
          <Input v-model.number="band.weight" type="number" class="w-24" :aria-label="t('dashboard.stream.raids.weight')" />
          <Input v-model="band.label" :aria-label="t('dashboard.stream.raids.label')" :placeholder="t('dashboard.stream.raids.label')" />
          <Button v-if="canEdit"
                  variant="ghost"
                  size="icon"
                  :aria-label="t('dashboard.stream.raids.remove')"
                  @click="removeAt(draft.viewerBands, index)"
          >
            <Cross class="size-5" />
          </Button>
        </div>
        <Button v-if="canEdit"
                variant="outline"
                size="sm"
                class="w-fit"
                @click="addBand"
        >
          <Plus class="size-5" />{{ t('dashboard.stream.raids.addBand') }}
        </Button>
      </section>

      <section class="flex flex-col gap-2">
        <span class="text-sm font-medium">{{ t('dashboard.stream.raids.recency') }}</span>
        <div class="grid gap-1.5 sm:w-1/2">
          <Label for="never-raided">{{ t('dashboard.stream.raids.neverRaidedWeight') }}</Label>
          <Input id="never-raided" v-model.number="draft.recency.neverRaidedWeight" type="number" />
        </div>
        <div v-for="(step, index) in draft.recency.steps" :key="index" class="flex items-center gap-2" data-recency-step>
          <Input v-model.number="step.withinDays" type="number" class="w-24" :aria-label="t('dashboard.stream.raids.withinDays')" />
          <Input v-model.number="step.weight" type="number" class="w-24" :aria-label="t('dashboard.stream.raids.weight')" />
          <Input v-model="step.note" :aria-label="t('dashboard.stream.raids.note')" :placeholder="t('dashboard.stream.raids.note')" />
          <Button v-if="canEdit"
                  variant="ghost"
                  size="icon"
                  :aria-label="t('dashboard.stream.raids.remove')"
                  @click="removeAt(draft.recency.steps, index)"
          >
            <Cross class="size-5" />
          </Button>
        </div>
        <Button v-if="canEdit"
                variant="outline"
                size="sm"
                class="w-fit"
                @click="addStep"
        >
          <Plus class="size-5" />{{ t('dashboard.stream.raids.addStep') }}
        </Button>
        <div class="grid gap-3 sm:grid-cols-2">
          <div class="grid gap-1.5">
            <Label for="older-weight">{{ t('dashboard.stream.raids.olderWeight') }}</Label>
            <Input id="older-weight" v-model.number="draft.recency.olderWeight" type="number" />
          </div>
          <div class="grid gap-1.5">
            <Label for="older-note">{{ t('dashboard.stream.raids.olderNote') }}</Label>
            <Input id="older-note" v-model="draft.recency.olderNote" />
          </div>
        </div>
      </section>

      <section class="flex flex-col gap-2">
        <span class="text-sm font-medium">{{ t('dashboard.stream.raids.balance') }}</span>
        <div class="grid gap-3 sm:grid-cols-3">
          <div class="grid gap-1.5">
            <Label for="owed-base">{{ t('dashboard.stream.raids.owedBaseWeight') }}</Label>
            <Input id="owed-base" v-model.number="draft.balance.owedBaseWeight" type="number" />
          </div>
          <div class="grid gap-1.5">
            <Label for="owed-extra">{{ t('dashboard.stream.raids.owedExtraWeight') }}</Label>
            <Input id="owed-extra" v-model.number="draft.balance.owedExtraWeight" type="number" />
          </div>
          <div class="grid gap-1.5">
            <Label for="reciprocal">{{ t('dashboard.stream.raids.reciprocalWeight') }}</Label>
            <Input id="reciprocal" v-model.number="draft.balance.reciprocalWeight" type="number" />
          </div>
        </div>
        <div class="grid gap-1.5 sm:w-1/3">
          <Label for="followed">{{ t('dashboard.stream.raids.followedWeight') }}</Label>
          <Input id="followed" v-model.number="draft.followedWeight" type="number" />
        </div>
      </section>

      <div v-if="canEdit" class="flex justify-end">
        <Button data-raid-rules-save :disabled="!dirty || save.isPending.value" @click="submit">
          {{ t('dashboard.stream.raids.save') }}
        </Button>
      </div>
    </div>
  </PanelCard>
</template>
