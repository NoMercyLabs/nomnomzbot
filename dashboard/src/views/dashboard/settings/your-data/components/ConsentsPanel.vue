<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The consents you gave: one row each, on while granted. Turning one off withdraws it and the row stays listed,
// so it can be given again.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useChangeConsent, useConsents, type ConsentRecord } from '@/api/yourData';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { usePanelState } from '@/composables/usePanelState';
import { errorText } from '@/lib/errors';

const { t, te } = useI18n();
const consents = useConsents();
const change = useChangeConsent();

const state = usePanelState(consents, (rows) =>
  rows.length === 0);
const rows = computed(() => consents.data.value ?? []);
const failure = ref<string | undefined>(undefined);

function label(record: ConsentRecord): string {
  const path = `dashboard.settings.yourData.consents.type.${record.consentType}`;

  return te(path)
    ? t(path)
    : record.consentType.replaceAll('_', ' ');
}

async function flip(record: ConsentRecord): Promise<void> {
  failure.value = undefined;

  try {
    await change.mutateAsync({ record, granted: record.status !== 'granted' });
  } catch (error) {
    failure.value = errorText(error, t);
  }
}
</script>

<template>
  <PanelCard
    data-panel="consents"
    :title="$t('dashboard.settings.yourData.consents.title')"
    :description="$t('dashboard.settings.yourData.consents.description')"
    :state="state"
    :empty-text="$t('dashboard.settings.yourData.consents.empty')"
    :error-text="$t('dashboard.settings.yourData.consents.loadFailed')"
    @retry="consents.refetch()"
  >
    <RowGroup>
      <SettingRow
        v-for="record in rows"
        :key="record.id"
        :data-consent="record.consentType"
        :title="label(record)"
        :description="$t('dashboard.settings.yourData.consents.basis', { basis: record.lawfulBasis })"
        :model-value="record.status === 'granted'"
        :disabled="change.isPending.value"
        @update:model-value="flip(record)"
      />
    </RowGroup>
    <p v-if="failure" role="alert" class="px-3 py-3 text-sm text-destructive">{{ failure }}</p>
  </PanelCard>
</template>
