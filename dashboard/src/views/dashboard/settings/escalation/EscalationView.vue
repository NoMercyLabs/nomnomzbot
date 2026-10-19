<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's escalation ladder: how repeat offenses step up from a warning to a timeout to a ban.
// The page edits a draft and sends the whole policy in one save; a refusal from the bot lands on the row it names.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { ApiError } from '@/api/errors';
import { useEscalationPolicy, useSaveEscalationPolicy } from '@/api/escalationPolicy';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import ReadOnlyScope from '@/components/layout/ReadOnlyScope.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import LadderPanel from './components/LadderPanel.vue';
import PolicyPanel from './components/PolicyPanel.vue';
import { sameDraft, toChange, toDraft, type EscalationDraft } from './escalationDraft';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const canManage = computed(() => can('moderation:escalation:write'));

const policy = useEscalationPolicy(channelId);
const save = useSaveEscalationPolicy(channelId);

const draft = ref<EscalationDraft | null>(null);
const rowErrors = ref<Partial<Record<string, string>>>({});
const saveError = ref('');

const saved = computed(() =>
  policy.data.value
    ? toDraft(policy.data.value)
    : null);
const shipped = computed(() => policy.data.value?.defaults ?? null);
const limits = computed(() => policy.data.value?.limits ?? null);

// A fresh policy replaces the draft only when the person has not edited it.
watch(
  saved,
  (next, previous) => {
    if (next === null) return;

    if (draft.value === null || !previous || sameDraft(draft.value, previous)) {
      draft.value = structuredClone(next);
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
  () =>
    draft.value === null ||
    shipped.value === null ||
    sameDraft(draft.value, toDraft(shipped.value)),
);
const state = computed(() => {
  if (policy.isError.value)
    return 'error';

  return draft.value === null
    ? 'loading'
    : 'ready';
});

function resetAll(): void {
  if (shipped.value !== null) {
    draft.value = toDraft(shipped.value);
  }
}

function refuse(error: Error): void {
  const violation = error instanceof ApiError
    ? error.violation
    : undefined;

  if (violation === undefined) {
    saveError.value = t('dashboard.settings.escalation.saveError');

    return;
  }

  rowErrors.value = { [violation.field]: t(violation.key) };
}

function submit(): void {
  if (draft.value !== null) {
    save.mutate(toChange(draft.value), { onError: refuse });
  }
}
</script>

<template>
  <PageHeader
    :title="$t('frame.nav.settingsPages.escalation')"
    :crumb="$t('dashboard.settings.escalation.crumb')"
  >
    <template v-if="canManage" #actions>
      <Button
        variant="ghost"
        size="sm"
        data-action="reset-all"
        :disabled="atDefaults"
        @click="resetAll"
      >
        {{ $t("common.resetToDefaults") }}
      </Button>
      <Button
        size="sm"
        data-action="save"
        :disabled="!dirty || save.isPending.value"
        @click="submit"
      >
        {{ $t("dashboard.settings.escalation.save") }}
      </Button>
    </template>
  </PageHeader>

  <PageBody>
    <div data-view="escalation" class="grid gap-4">
      <PanelCard
        v-if="state !== 'ready'"
        :title="$t('frame.nav.settingsPages.escalation')"
        tier="quiet"
        pad
        :state="state"
        :error-text="$t('dashboard.settings.escalation.loadError')"
        @retry="policy.refetch()"
      />
      <ReadOnlyScope v-else-if="draft && shipped && limits" :read-only="!canManage">
        <p v-if="saveError" role="alert" data-save-error class="mb-4 text-sm text-destructive">
          {{ saveError }}
        </p>
        <div class="grid gap-4">
          <PolicyPanel v-model="draft" :shipped="shipped" :limits="limits" :errors="rowErrors" />
          <LadderPanel v-model="draft" :limits="limits" :errors="rowErrors" />
        </div>
      </ReadOnlyScope>
    </div>
  </PageBody>
</template>
