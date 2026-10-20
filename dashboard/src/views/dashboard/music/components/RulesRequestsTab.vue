<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Requests tab of the request rules: who may ask, the queue limits, the limits per person.
// The edits live in the parent's draft; this tab only shows and changes it.
import DurationInput from '@/components/layout/DurationInput.vue';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Input } from '@/components/ui/input';
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group';
import { musicRoleRungs, musicTrustLevels } from '@/api/vocabulary';
import { useCan } from '@/composables/useCan';

import RuleRow from './RuleRow.vue';
import RuleSection from './RuleSection.vue';
import {
  perStreamLimits,
  queueLimits,
  requestLimits,
  songSecondsLimits,
  type RuleDraft,
} from './requestRules';

defineProps<{ valid: boolean }>();

const draft = defineModel<RuleDraft>({ required: true });
const { can } = useCan();
</script>

<template>
  <fieldset class="contents" :disabled="!can('music:config:write')">
    <div class="grid gap-5">
      <RuleSection :title="$t('dashboard.music.rules.who')">
        <RadioGroup v-model="draft.minTrustLevel" as-child>
          <RowGroup>
            <label
              v-for="level in musicTrustLevels"
              :key="level"
              class="flex min-h-11 items-center gap-3 rounded-[8px] px-3 py-2 text-sm font-medium hover:bg-foreground/5"
            >
              <RadioGroupItem :value="level" />
              {{ $t(`dashboard.music.rules.levels.${level}`) }}
            </label>
          </RowGroup>
        </RadioGroup>
      </RuleSection>
      <RuleSection :title="$t('dashboard.music.rules.queue')">
        <RowGroup>
          <RuleRow :title="$t('dashboard.music.rules.queueSize')">
            <Input
              v-model.number="draft.maxQueueSize"
              name="queue-size"
              type="number"
              :min="queueLimits.min"
              :max="queueLimits.max"
              step="1"
              :aria-invalid="!valid || undefined"
              class="w-20"
            />
          </RuleRow>
          <RuleRow :title="$t('dashboard.music.rules.songSeconds')">
            <DurationInput
              v-model="draft.maxSongSeconds"
              name="song-seconds"
              :min="songSecondsLimits.min"
              :max="songSecondsLimits.max"
              class="w-20"
            />
          </RuleRow>
          <RuleRow :title="$t('dashboard.music.rules.allowExplicit')">
            <GatedSwitch
              v-model="draft.allowExplicit"
              action="music:config:write"
              data-setting="allow-explicit"
            />
          </RuleRow>
        </RowGroup>
      </RuleSection>
      <RuleSection :title="$t('dashboard.music.rules.perPersonSection')">
        <RowGroup>
          <RuleRow
            :title="$t('dashboard.music.rules.perPerson')"
            :description="$t('dashboard.music.rules.perPersonHelp')"
          >
            <Input
              v-model.number="draft.perPerson"
              name="songs-per-person"
              type="number"
              :min="requestLimits.min"
              :max="requestLimits.max"
              step="1"
              :aria-invalid="!valid || undefined"
              class="w-20"
            />
          </RuleRow>
          <RuleRow
            :title="$t('dashboard.music.rules.perStream')"
            :description="$t('dashboard.music.rules.perStreamHelp')"
          >
            <Input
              v-model.number="draft.perStreamLimit"
              name="per-stream-limit"
              type="number"
              :min="perStreamLimits.min"
              :max="perStreamLimits.max"
              step="1"
              :aria-invalid="!valid || undefined"
              class="w-20"
            />
          </RuleRow>
        </RowGroup>
      </RuleSection>
      <RuleSection
        :title="$t('dashboard.music.rules.byRole')"
        :description="$t('dashboard.music.rules.perRoleHelp')"
      >
        <RowGroup>
          <RuleRow
            v-for="rung in musicRoleRungs"
            :key="rung"
            :title="$t(`dashboard.music.rules.roles.${rung}`)"
          >
            <Input
              v-model="draft.roleCaps[rung]"
              :name="`songs-role-${rung}`"
              type="number"
              :min="requestLimits.min"
              :max="requestLimits.max"
              step="1"
              :placeholder="$t('dashboard.music.rules.sameAsPerPerson')"
              :aria-invalid="!valid || undefined"
              class="w-20"
            />
          </RuleRow>
        </RowGroup>
      </RuleSection>
    </div>
  </fieldset>
</template>
