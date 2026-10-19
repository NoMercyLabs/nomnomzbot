<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The ladder: one row per rung, each with the offense number it starts at, the action it takes and, for a
// timeout, how long. A refusal from the bot lands under the rung it names. The step count stays inside the limits.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import Plus from '~icons/nnz/add';
import Trash from '~icons/nnz/trash';

import type { EscalationPolicy } from '@/api/escalationPolicy';
import IconAction from '@/components/layout/IconAction.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import DurationInput from '@/components/layout/DurationInput.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';

import {
  nextStep,
  stepActions,
  withAction,
  type DraftStep,
  type EscalationDraft,
  type StepAction,
} from '../escalationDraft';

const props = defineProps<{
  limits: EscalationPolicy['limits'];
  errors: Partial<Record<string, string>>;
}>();

const draft = defineModel<EscalationDraft>({ required: true });
const { t } = useI18n();
const { can } = useCan();

const options = computed(() =>
  stepActions.map((value) => ({
    value,
    label: t(`dashboard.settings.escalation.actions.${value}`),
  })),
);
const canAdd = computed(() => draft.value.ladder.length < Number(props.limits.maxLadderSteps));
const canRemove = computed(() => draft.value.ladder.length > Number(props.limits.minLadderSteps));

function stepErrors(index: number): string[] {
  const keys = ['atOffense', 'action', 'timeoutSeconds'] as const;

  return keys
    .map((key) => props.errors[`ladder[${index}].${key}`])
    .filter((message): message is string => message !== undefined);
}

function setOffense(step: DraftStep, typed: string | number): void {
  step.atOffense = Number(typed);
}

function setAction(index: number, action: StepAction): void {
  draft.value.ladder[index] = withAction(
    draft.value.ladder[index]!,
    action,
    draft.value.defaultTimeoutSeconds,
  );
}

function addStep(): void {
  draft.value.ladder.push(nextStep(draft.value));
}

function removeStep(index: number): void {
  draft.value.ladder.splice(index, 1);
}
</script>

<template>
  <PanelCard
    tier="quiet"
    pad
    data-group="ladder"
    :title="$t('dashboard.settings.escalation.ladder.title')"
    :description="$t('dashboard.settings.escalation.ladder.description')"
  >
    <p v-if="errors.ladder" data-row-error role="alert" class="mb-2 text-sm text-destructive">
      {{ errors.ladder }}
    </p>
    <RowGroup>
      <div
        v-for="(step, index) in draft.ladder"
        :key="index"
        :data-step="index"
        class="min-h-11 rounded-[8px] px-3 py-2"
      >
        <div class="flex flex-wrap items-center gap-3">
          <label class="flex items-center gap-2 text-sm font-medium">
            {{ $t("dashboard.settings.escalation.ladder.atOffense") }}
            <Input
              type="number"
              inputmode="numeric"
              data-step-field="atOffense"
              class="w-20 text-end"
              :model-value="step.atOffense"
              :min="1"
              :aria-invalid="errors[`ladder[${index}].atOffense`] ? 'true' : undefined"
              @update:model-value="setOffense(step, $event)"
            />
          </label>
          <SegmentedControl
            :model-value="step.action"
            :label="$t('dashboard.settings.escalation.ladder.action')"
            :options="options"
            @update:model-value="setAction(index, $event)"
          />
          <DurationInput
            v-if="step.action === 'timeout'"
            :model-value="step.timeoutSeconds ?? 0"
            data-step-field="timeoutSeconds"
            class="w-24 text-end"
            :aria-label="$t('dashboard.settings.escalation.ladder.timeoutSeconds')"
            :min="Number(limits.minTimeoutSeconds)"
            :max="Number(limits.maxTimeoutSeconds)"
            @update:model-value="step.timeoutSeconds = $event"
          />
          <IconAction
            class="ms-auto"
            data-action="remove-step"
            :label="$t('dashboard.settings.escalation.ladder.remove')"
            :disabled="!canRemove"
            @click="removeStep(index)"
          >
            <Trash class="size-5" />
          </IconAction>
        </div>
        <p
          v-for="message in stepErrors(index)"
          :key="message"
          data-row-error
          role="alert"
          class="mt-1 text-sm text-destructive"
        >
          {{ message }}
        </p>
      </div>
    </RowGroup>
    <Button
      v-if="can('moderation:escalation:write')"
      variant="outline"
      size="sm"
      class="mt-3"
      data-action="add-step"
      :disabled="!canAdd"
      @click="addStep"
    >
      <Plus class="size-5" />
      {{ $t("dashboard.settings.escalation.ladder.add") }}
    </Button>
  </PanelCard>
</template>
