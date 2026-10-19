<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One group of trust fields as a panel: a switch for the toggle field, a number row for every other field,
// each with its shipped default beside the title. The weights group also shows the live sum of its four fields.
import NumberRow from '@/components/layout/NumberRow.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';

import type { NumberFieldKey, TrustDraft, TrustGroup } from '../trustFields';

defineProps<{
  group: TrustGroup;
  shipped: TrustDraft | null;
  errors: Partial<Record<string, string>>;
  sum: number;
}>();

const draft = defineModel<TrustDraft>({ required: true });

function setNumber(key: NumberFieldKey, value: number): void {
  draft.value[key] = value;
}
</script>

<template>
  <PanelCard
    tier="quiet"
    pad
    :data-group="group.key"
    :title="$t(`dashboard.settings.trustPolicy.groups.${group.key}.title`)"
    :description="$t(`dashboard.settings.trustPolicy.groups.${group.key}.description`)"
  >
    <p v-if="group.key === 'weights'" data-weight-sum class="mb-2 text-sm text-muted-foreground">
      {{ $t('dashboard.settings.trustPolicy.weightSum', { sum }) }}
    </p>
    <RowGroup>
      <template v-for="field in group.fields" :key="field.key">
        <SettingRow
          v-if="'toggle' in field"
          v-model="draft.reputationBoostEnabled"
          :data-field="field.key"
          :title="$t(`dashboard.settings.trustPolicy.fields.${field.key}.title`)"
          :description="$t(`dashboard.settings.trustPolicy.fields.${field.key}.help`)"
        />
        <NumberRow
          v-else
          :data-field="field.key"
          :model-value="draft[field.key]"
          :title="$t(`dashboard.settings.trustPolicy.fields.${field.key}.title`)"
          :description="$t(`dashboard.settings.trustPolicy.fields.${field.key}.help`)"
          :hint="shipped ? $t('dashboard.settings.trustPolicy.default', { value: shipped[field.key] }) : undefined"
          :default-value="shipped ? shipped[field.key] : undefined"
          :error="errors[field.key]"
          :step="field.step"
          @update:model-value="setNumber(field.key, $event)"
        />
      </template>
    </RowGroup>
  </PanelCard>
</template>
