<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Requests tab of the request rules: the queue limits, the limits per person.
// The edits live in the parent's draft; this tab only shows and changes it.
import DurationInput from '@/components/layout/DurationInput.vue';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Input } from '@/components/ui/input';
import { musicRoleRungs } from '@/api/vocabulary';
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
