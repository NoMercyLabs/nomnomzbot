<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// How this channel picks who to raid. Every point comes from these rules, per channel; a new channel starts neutral.
// Each rule reads as one sentence with labeled controls. The form edits a copy. Save is the one primary action and
// sends the whole set; the server refuses a set that breaks a limit and keeps the old one.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import type { ApiFieldProblem } from '@/api/errors';
import { useRaidRules, useSaveRaidRules, type RaidRules } from '@/api/schedule';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { refusalProblems } from '@/lib/errors';
import CategoryPicker from '@/views/dashboard/stream/CategoryPicker.vue';
import PointsControl from '@/views/dashboard/stream/raidRules/PointsControl.vue';
import RuleSection from '@/views/dashboard/stream/raidRules/RuleSection.vue';
import WordChips from '@/views/dashboard/stream/raidRules/WordChips.vue';
import RaidProblemLines from '@/views/dashboard/stream/RaidProblemLines.vue';
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
const problems = ref<ApiFieldProblem[]>([]);

watch(
  () => query.data.value,
  (rules: RaidRules | null | undefined) => {
    const json = JSON.stringify(rules ?? null);

    draft.value = JSON.parse(json);
  },
  { immediate: true },
);

const dirty = computed(() => JSON.stringify(draft.value) !== JSON.stringify(query.data.value));

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

function refuse(error: Error): void {
  problems.value = refusalProblems(error);
}

function submit(): void {
  if (draft.value) {
    problems.value = [];
    save.mutate(draft.value, { onError: refuse });
  }
}
</script>

<template>
  <PanelCard
    :title="t('dashboard.stream.raids.title')"
    :description="t('dashboard.stream.raids.description')"
    tier="lead"
    pad
    :state="state"
    :error-text="t('dashboard.stream.raids.error')"
    @retry="query.refetch()"
  >
    <div v-if="draft" :inert="!canEdit" class="flex flex-col gap-8">
      <p data-raid-intro class="text-sm">
        {{ t("dashboard.stream.raids.intro") }}
      </p>

      <RuleSection
        :title="t('dashboard.stream.raids.preferredCategories')"
        :hint="t('dashboard.stream.raids.preferredCategoriesHint')"
      >
        <template v-for="(rule, index) in draft.preferredCategories" :key="index">
          <div
            class="flex flex-col gap-3 rounded-lg bg-muted/40 p-3"
            data-category-rule
          >
            <div class="flex items-start gap-2">
              <div class="flex min-w-0 flex-1 flex-col gap-1.5" data-category-picker>
                <span class="text-sm text-muted-foreground">{{ t("dashboard.stream.raids.whenTheyPlay") }}</span>
                <CategoryPicker
                  v-if="canEdit"
                  v-model="rule.name"
                  :channel-id="channelId"
                  :recent="[]"
                />
                <p v-else class="text-sm font-medium">{{ rule.name }}</p>
              </div>
              <Button
                v-if="canEdit"
                variant="ghost"
                size="icon"
                :aria-label="t('dashboard.stream.raids.remove')"
                @click="removeAt(draft.preferredCategories, index)"
              >
                <Cross class="size-5" />
              </Button>
            </div>
            <div class="flex flex-wrap items-center gap-2">
              <span class="text-sm">{{ t("dashboard.stream.raids.gives") }}</span>
              <PointsControl
                v-model="rule.weight"
                :label="t('dashboard.stream.raids.weight')"
                :can-edit="canEdit"
              />
              <span class="text-sm">{{ t("dashboard.stream.raids.shownAs") }}</span>
              <Input
                v-model="rule.label"
                class="w-56"
                :aria-label="t('dashboard.stream.raids.label')"
                :placeholder="t('dashboard.stream.raids.reasonPlaceholder')"
              />
            </div>
          </div>
          <RaidProblemLines :problems="problems" field="category" :subject="rule.name" />
        </template>
        <Button v-if="canEdit"
                variant="outline"
                size="sm"
                class="w-fit"
                data-category-add
                @click="addCategory"
        >
          <Plus class="size-5" />{{ t("dashboard.stream.raids.addCategory") }}
        </Button>
        <div class="flex flex-wrap items-center gap-2">
          <span class="text-sm">{{ t("dashboard.stream.raids.otherCategory") }}</span>
          <PointsControl
            id="other-weight"
            v-model="draft.otherCategoryWeight"
            :label="t('dashboard.stream.raids.weight')"
            :can-edit="canEdit"
          >
            <RaidProblemLines :problems="problems" field="otherCategory" />
          </PointsControl>
          <span class="text-sm">{{ t("dashboard.stream.raids.shownAs") }}</span>
          <Input
            id="other-label"
            v-model="draft.otherCategoryLabel"
            class="w-56"
            :aria-label="t('dashboard.stream.raids.label')"
            :placeholder="t('dashboard.stream.raids.reasonPlaceholder')"
          />
        </div>
      </RuleSection>

      <RuleSection
        :title="t('dashboard.stream.raids.keywords')"
        :hint="t('dashboard.stream.raids.keywordsHint')"
      >
        <template v-for="(rule, index) in draft.keywords" :key="index">
          <div
            class="flex flex-col gap-3 rounded-lg bg-muted/40 p-3"
            data-keyword-rule
          >
            <div class="flex flex-wrap items-center gap-x-3 gap-y-2">
              <span class="text-sm">{{ t("dashboard.stream.raids.whenTheStream") }}</span>
              <LabeledSwitch
                v-model="rule.matchTitle"
                :label="t('dashboard.stream.raids.matchTitle')"
                action="live-ops:raids:write"
              />
              <LabeledSwitch
                v-model="rule.matchTags"
                :label="t('dashboard.stream.raids.matchTags')"
                action="live-ops:raids:write"
              />
              <span class="text-sm">{{ t("dashboard.stream.raids.containAny") }}</span>
              <Button
                v-if="canEdit"
                variant="ghost"
                size="icon"
                class="ml-auto"
                :aria-label="t('dashboard.stream.raids.remove')"
                @click="removeAt(draft.keywords, index)"
              >
                <Cross class="size-5" />
              </Button>
            </div>
            <WordChips v-model="rule.words" :can-edit="canEdit" />
            <div class="flex flex-wrap items-center gap-2">
              <span class="text-sm">{{ t("dashboard.stream.raids.gives") }}</span>
              <PointsControl
                v-model="rule.weight"
                :label="t('dashboard.stream.raids.weight')"
                :can-edit="canEdit"
              />
              <span class="text-sm">{{ t("dashboard.stream.raids.shownAs") }}</span>
              <Input
                v-model="rule.label"
                class="w-56"
                :aria-label="t('dashboard.stream.raids.label')"
                :placeholder="t('dashboard.stream.raids.reasonPlaceholder')"
              />
            </div>
          </div>
          <RaidProblemLines :problems="problems" field="keyword" :subject="rule.label" />
        </template>
        <Button v-if="canEdit"
                variant="outline"
                size="sm"
                class="w-fit"
                data-keyword-add
                @click="addKeyword"
        >
          <Plus class="size-5" />{{ t("dashboard.stream.raids.addKeyword") }}
        </Button>
      </RuleSection>

      <RuleSection
        :title="t('dashboard.stream.raids.viewerBands')"
        :hint="t('dashboard.stream.raids.viewerBandsHint')"
      >
        <template v-for="(band, index) in draft.viewerBands" :key="index">
          <div
            class="flex flex-wrap items-center gap-2"
            data-viewer-band
          >
            <Input
              v-model.number="band.min"
              type="number"
              class="w-24"
              :aria-label="t('dashboard.stream.raids.min')"
            />
            <span class="text-sm">{{ t("dashboard.stream.raids.to") }}</span>
            <Input
              v-model.number="band.max"
              type="number"
              class="w-24"
              :aria-label="t('dashboard.stream.raids.max')"
            />
            <span class="text-sm">{{ t("dashboard.stream.raids.viewersGive") }}</span>
            <PointsControl
              v-model="band.weight"
              :label="t('dashboard.stream.raids.weight')"
              :can-edit="canEdit"
            />
            <span class="text-sm">{{ t("dashboard.stream.raids.shownAs") }}</span>
            <Input
              v-model="band.label"
              class="w-48"
              :aria-label="t('dashboard.stream.raids.label')"
              :placeholder="t('dashboard.stream.raids.reasonPlaceholder')"
            />
            <Button
              v-if="canEdit"
              variant="ghost"
              size="icon"
              :aria-label="t('dashboard.stream.raids.remove')"
              @click="removeAt(draft.viewerBands, index)"
            >
              <Cross class="size-5" />
            </Button>
          </div>
          <RaidProblemLines :problems="problems" field="band" :subject="band.label" />
        </template>
        <Button v-if="canEdit"
                variant="outline"
                size="sm"
                class="w-fit"
                data-band-add
                @click="addBand"
        >
          <Plus class="size-5" />{{ t("dashboard.stream.raids.addBand") }}
        </Button>
      </RuleSection>

      <RuleSection
        :title="t('dashboard.stream.raids.recency')"
        :hint="t('dashboard.stream.raids.recencyHint')"
      >
        <div class="flex flex-wrap items-center gap-2">
          <span class="text-sm">{{ t("dashboard.stream.raids.neverRaided") }}</span>
          <PointsControl
            id="never-raided"
            v-model="draft.recency.neverRaidedWeight"
            :label="t('dashboard.stream.raids.weight')"
            :can-edit="canEdit"
          >
            <RaidProblemLines :problems="problems" field="neverRaided" />
          </PointsControl>
        </div>
        <template v-for="(step, index) in draft.recency.steps" :key="index">
          <div
            class="flex flex-wrap items-center gap-2"
            data-recency-step
          >
            <span class="text-sm">{{ t("dashboard.stream.raids.raidedLessThan") }}</span>
            <Input
              v-model.number="step.withinDays"
              type="number"
              class="w-20"
              :aria-label="t('dashboard.stream.raids.withinDays')"
            />
            <span class="text-sm">{{ t("dashboard.stream.raids.daysAgoGives") }}</span>
            <PointsControl
              v-model="step.weight"
              :label="t('dashboard.stream.raids.weight')"
              :can-edit="canEdit"
            />
            <span class="text-sm">{{ t("dashboard.stream.raids.shownAs") }}</span>
            <Input
              v-model="step.note"
              class="w-48"
              :aria-label="t('dashboard.stream.raids.note')"
              :placeholder="t('dashboard.stream.raids.reasonPlaceholder')"
            />
            <Button
              v-if="canEdit"
              variant="ghost"
              size="icon"
              :aria-label="t('dashboard.stream.raids.remove')"
              @click="removeAt(draft.recency.steps, index)"
            >
              <Cross class="size-5" />
            </Button>
          </div>
          <RaidProblemLines :problems="problems" field="step" :subject="step.note" />
        </template>
        <Button v-if="canEdit"
                variant="outline"
                size="sm"
                class="w-fit"
                data-step-add
                @click="addStep"
        >
          <Plus class="size-5" />{{ t("dashboard.stream.raids.addStep") }}
        </Button>
        <div class="flex flex-wrap items-center gap-2">
          <span class="text-sm">{{ t("dashboard.stream.raids.olderRaids") }}</span>
          <PointsControl
            id="older-weight"
            v-model="draft.recency.olderWeight"
            :label="t('dashboard.stream.raids.weight')"
            :can-edit="canEdit"
          >
            <RaidProblemLines :problems="problems" field="olderRaids" />
          </PointsControl>
          <span class="text-sm">{{ t("dashboard.stream.raids.shownAs") }}</span>
          <Input
            id="older-note"
            v-model="draft.recency.olderNote"
            class="w-48"
            :aria-label="t('dashboard.stream.raids.note')"
            :placeholder="t('dashboard.stream.raids.reasonPlaceholder')"
          />
        </div>
      </RuleSection>

      <RuleSection
        :title="t('dashboard.stream.raids.balance')"
        :hint="t('dashboard.stream.raids.balanceHint')"
      >
        <div class="flex flex-wrap items-center gap-2">
          <span class="text-sm">{{ t("dashboard.stream.raids.owedBase") }}</span>
          <PointsControl
            id="owed-base"
            v-model="draft.balance.owedBaseWeight"
            :label="t('dashboard.stream.raids.weight')"
            :can-edit="canEdit"
          >
            <RaidProblemLines :problems="problems" field="owedBase" />
          </PointsControl>
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <span class="text-sm">{{ t("dashboard.stream.raids.owedExtra") }}</span>
          <PointsControl
            id="owed-extra"
            v-model="draft.balance.owedExtraWeight"
            :label="t('dashboard.stream.raids.weight')"
            :can-edit="canEdit"
          >
            <RaidProblemLines :problems="problems" field="owedExtra" />
          </PointsControl>
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <span class="text-sm">{{ t("dashboard.stream.raids.reciprocal") }}</span>
          <PointsControl
            id="reciprocal"
            v-model="draft.balance.reciprocalWeight"
            :label="t('dashboard.stream.raids.weight')"
            :can-edit="canEdit"
          >
            <RaidProblemLines :problems="problems" field="reciprocal" />
          </PointsControl>
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <span class="text-sm">{{ t("dashboard.stream.raids.followed") }}</span>
          <PointsControl
            id="followed"
            v-model="draft.followedWeight"
            :label="t('dashboard.stream.raids.weight')"
            :can-edit="canEdit"
          >
            <RaidProblemLines :problems="problems" field="followed" />
          </PointsControl>
        </div>
      </RuleSection>
    </div>
    <template v-if="draft && canEdit" #footer>
      <Button
        data-raid-rules-save
        class="ml-auto"
        :variant="dirty ? 'default' : 'outline'"
        :disabled="!dirty || save.isPending.value"
        @click="submit"
      >
        {{ t("dashboard.stream.raids.save") }}
      </Button>
    </template>
  </PanelCard>
</template>
