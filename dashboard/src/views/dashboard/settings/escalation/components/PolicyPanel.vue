<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The policy's own settings: whether the ladder runs, whether AutoMod holds count as offenses, how long an
// offense counts, and the length a timeout step takes when it names none. Defaults and ranges come from the bot.
import { useId } from 'vue';

import type { EscalationPolicy } from '@/api/escalationPolicy';
import DurationInput from '@/components/layout/DurationInput.vue';
import NumberRow from '@/components/layout/NumberRow.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import RowText from '@/components/layout/RowText.vue';
import SettingRow from '@/components/layout/SettingRow.vue';

import type { EscalationDraft } from '../escalationDraft';

defineProps<{
  shipped: EscalationPolicy['defaults'];
  limits: EscalationPolicy['limits'];
  errors: Partial<Record<string, string>>;
}>();

const draft = defineModel<EscalationDraft>({ required: true });
const timeoutId = useId();
</script>

<template>
  <PanelCard
    tier="quiet"
    pad
    data-group="policy"
    :title="$t('dashboard.settings.escalation.policy.title')"
    :description="$t('dashboard.settings.escalation.policy.description')"
  >
    <RowGroup>
      <SettingRow
        v-model="draft.isEnabled"
        data-field="isEnabled"
        :title="$t('dashboard.settings.escalation.fields.isEnabled.title')"
        :description="$t('dashboard.settings.escalation.fields.isEnabled.help')"
        :hint="
          $t('dashboard.settings.escalation.defaultState', {
            state: $t(shipped.isEnabled ? 'common.state.on' : 'common.state.off'),
          })
        "
      />
      <SettingRow
        v-model="draft.countAutoModViolations"
        data-field="countAutoModViolations"
        :title="$t('dashboard.settings.escalation.fields.countAutoModViolations.title')"
        :description="$t('dashboard.settings.escalation.fields.countAutoModViolations.help')"
        :hint="
          $t('dashboard.settings.escalation.defaultState', {
            state: $t(shipped.countAutoModViolations ? 'common.state.on' : 'common.state.off'),
          })
        "
      />
      <NumberRow
        v-model="draft.offenseWindowHours"
        data-field="offenseWindowHours"
        :title="$t('dashboard.settings.escalation.fields.offenseWindowHours.title')"
        :description="$t('dashboard.settings.escalation.fields.offenseWindowHours.help')"
        :hint="$t('dashboard.settings.escalation.default', { value: shipped.offenseWindowHours })"
        :default-value="Number(shipped.offenseWindowHours)"
        :min="Number(limits.minOffenseWindowHours)"
        :max="Number(limits.maxOffenseWindowHours)"
        :error="errors.offenseWindowHours"
      />
      <div data-field="defaultTimeoutSeconds" class="min-h-11 rounded-[8px] px-3 py-2">
        <div class="flex items-center gap-3">
          <span class="min-w-0 flex-1">
            <RowText
              :title="$t('dashboard.settings.escalation.fields.defaultTimeoutSeconds.title')"
              :title-id="timeoutId"
              :description="$t('dashboard.settings.escalation.fields.defaultTimeoutSeconds.help')"
              :hint="
                $t('dashboard.settings.escalation.default', {
                  value: shipped.defaultTimeoutSeconds,
                })
              "
            />
          </span>
          <DurationInput
            v-model="draft.defaultTimeoutSeconds"
            class="w-28 flex-none text-end"
            :aria-labelledby="timeoutId"
            :min="Number(limits.minTimeoutSeconds)"
            :max="Number(limits.maxTimeoutSeconds)"
          />
        </div>
        <p
          v-if="errors.defaultTimeoutSeconds"
          data-row-error
          role="alert"
          class="mt-1 text-sm text-destructive"
        >
          {{ errors.defaultTimeoutSeconds }}
        </p>
      </div>
    </RowGroup>
  </PanelCard>
</template>
