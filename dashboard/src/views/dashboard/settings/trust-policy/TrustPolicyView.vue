<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's trust tuning: every number behind a viewer's trust score, the tier limits and the heat.
// The page edits a draft and sends all fields in one save; a refusal from the bot lands on the row it names.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useSaveTrustPolicy, useTrustPolicy, useTrustPolicyDefaults } from '@/api/trustPolicy';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import ReadOnlyScope from '@/components/layout/ReadOnlyScope.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { refusalProblems } from '@/lib/errors';

import TrustGroupPanel from './components/TrustGroupPanel.vue';
import { sameDraft, toDraft, trustGroups, weightSum, type TrustDraft } from './trustFields';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const canManage = computed(() => can('trust:policy:manage'));

const policy = useTrustPolicy(channelId);
const defaults = useTrustPolicyDefaults(channelId);
const save = useSaveTrustPolicy(channelId);

const draft = ref<TrustDraft | null>(null);
const rowErrors = ref<Partial<Record<string, string>>>({});
const saveError = ref('');

const saved = computed(() =>
  policy.data.value
    ? toDraft(policy.data.value)
    : null);
const shipped = computed(() =>
  defaults.data.value
    ? toDraft(defaults.data.value)
    : null);

// A fresh policy replaces the draft only when the person has not edited it.
watch(
  saved,
  (next, previous) => {
    if (next === null) return;

    if (draft.value === null || !previous || sameDraft(draft.value, previous)) {
      draft.value = { ...next };
    }
  },
  { immediate: true },
);

watch(
  draft,
  () => {
    rowErrors.value = {};
    saveError.value = '';
  },
  { deep: true },
);

const dirty = computed(
  () => draft.value !== null && saved.value !== null && !sameDraft(draft.value, saved.value),
);
const atDefaults = computed(
  () => draft.value === null || shipped.value === null || sameDraft(draft.value, shipped.value),
);
const sum = computed(() =>
  draft.value
    ? weightSum(draft.value)
    : 0);
const state = computed(() => {
  if (policy.isError.value)
    return 'error';

  return draft.value === null
    ? 'loading'
    : 'ready';
});

function resetAll(): void {
  if (shipped.value !== null) {
    draft.value = { ...shipped.value };
  }
}

function refuse(error: Error): void {
  const problems = refusalProblems(error);

  if (problems.length === 0) {
    saveError.value = t('dashboard.settings.trustPolicy.saveError');

    return;
  }

  const lines = problems.map((problem) => [problem.field, t(problem.reason)] as const);

  rowErrors.value = Object.fromEntries(lines);
}

function submit(): void {
  if (draft.value !== null) {
    save.mutate(draft.value, { onError: refuse });
  }
}
</script>

<template>
  <PageHeader
    :title="$t('frame.nav.settingsPages.trustPolicy')"
    :crumb="$t('dashboard.settings.trustPolicy.crumb')"
  >
    <template v-if="canManage" #actions>
      <Button variant="ghost"
              size="sm"
              data-action="reset-all"
              :disabled="atDefaults"
              @click="resetAll"
      >
        {{ $t('common.resetToDefaults') }}
      </Button>
      <Button size="sm" data-action="save" :disabled="!dirty || save.isPending.value" @click="submit">
        {{ $t('dashboard.settings.trustPolicy.save') }}
      </Button>
    </template>
  </PageHeader>

  <PageBody>
    <div data-view="trust-policy" class="grid gap-4">
      <PanelCard
        v-if="state !== 'ready'"
        :title="$t('frame.nav.settingsPages.trustPolicy')"
        tier="quiet"
        pad
        :state="state"
        :error-text="$t('dashboard.settings.trustPolicy.loadError')"
        @retry="policy.refetch()"
      />
      <ReadOnlyScope v-else-if="draft" :read-only="!canManage">
        <p v-if="saveError" role="alert" data-save-error class="mb-4 text-sm text-destructive">{{ saveError }}</p>
        <div class="grid gap-4">
          <TrustGroupPanel
            v-for="group in trustGroups"
            :key="group.key"
            v-model="draft"
            :group="group"
            :shipped="shipped"
            :errors="rowErrors"
            :sum="sum"
          />
        </div>
      </ReadOnlyScope>
    </div>
  </PageBody>
</template>
