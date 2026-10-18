<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Who may do what: every action of the bot with the lowest role that may do it in this channel.
// A protected action cannot go below its floor; the bot clamps it and the choices below the floor are off.
import Reset from '~icons/nnz/arrow-counter-clockwise';
import Locked from '~icons/nnz/locked';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  rungOf,
  rungs,
  useActionMatrix,
  useResetActionLevel,
  useSetActionLevel,
  type ActionPermission,
  type RungName,
} from '@/api/permissions';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { errorText } from '@/lib/errors';

import OptionSelect, { type Option } from './OptionSelect.vue';

defineProps<{ canManage: boolean }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const matrix = useActionMatrix(channelId);
const setLevel = useSetActionLevel(channelId);
const reset = useResetActionLevel(channelId);
const state = usePanelState(matrix, (rows) =>
  rows.length === 0);
const error = ref<string | undefined>(undefined);

const planes = ['Community', 'Management'] as const;

const byPlane = computed(() =>
  planes
    .map((plane) => ({
      plane,
      rows: (matrix.data.value ?? []).filter((row) => Number(row.plane) === planes.indexOf(plane)),
    }))
    .filter((group) => group.rows.length > 0));

const rungLabel = (name: string): string => t(`dashboard.settings.permissions.role.${name}`);

function optionsFor(row: ActionPermission): Option[] {
  return rungs.map((rung) => ({
    value: rung.name,
    label: rungLabel(rung.name),
    disabled: rung.level < Number(row.floorLevel),
  }));
}

// The bot's danger tiers are Critical (0), Tos (1) and Low (2); only Low may be freely lowered.
const lowTier = 2;
const isProtected = (row: ActionPermission): boolean => Number(row.floorTier) !== lowTier;

async function run(action: Promise<void>): Promise<void> {
  error.value = undefined;

  try {
    await action;
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}

const changeLevel = (actionKey: string, rung: RungName): Promise<void> =>
  run(setLevel.mutateAsync({ actionKey, rung }));
</script>

<template>
  <PanelCard
    data-panel="action-matrix"
    :title="$t('dashboard.settings.permissions.matrix.title')"
    :description="$t('dashboard.settings.permissions.matrix.description')"
    :state="state"
    :empty-text="$t('dashboard.settings.permissions.matrix.empty')"
    :error-text="$t('dashboard.settings.permissions.matrix.loadFailed')"
    @retry="matrix.refetch()"
  >
    <section v-for="group in byPlane" :key="group.plane" class="pb-3">
      <h3 class="pb-1 text-sm font-medium text-muted-foreground">
        {{ $t(`dashboard.settings.permissions.matrix.plane.${group.plane}`) }}
      </h3>
      <RowGroup>
        <SummaryRow
          v-for="row in group.rows"
          :key="row.actionKey"
          :data-action="row.actionKey"
          :title="row.actionKey"
          :detail="row.description ?? undefined"
          :meta="$t('dashboard.settings.permissions.matrix.floor', { role: rungLabel(rungOf(row.floorLevel)) })"
        >
          <template #end>
            <Badge v-if="isProtected(row)" variant="outline" data-protected>
              <Locked class="size-4" />
              {{ $t('dashboard.settings.permissions.matrix.protected') }}
            </Badge>
            <OptionSelect
              data-action-level
              :label="$t('dashboard.settings.permissions.matrix.levelOf', { action: row.actionKey })"
              :model-value="rungOf(row.effectiveLevel)"
              :options="optionsFor(row)"
              :disabled="!canManage"
              @update:model-value="changeLevel(row.actionKey, $event as RungName)"
            />
            <Button
              variant="ghost"
              size="icon"
              data-action-reset
              :class="row.overrideLevel != null && canManage ? '' : 'invisible'"
              :aria-label="$t('dashboard.settings.permissions.matrix.reset', { action: row.actionKey })"
              @click="run(reset.mutateAsync(row.actionKey))"
            >
              <Reset class="size-5" />
            </Button>
          </template>
        </SummaryRow>
      </RowGroup>
    </section>
    <p v-if="error" role="alert" class="pt-3 text-sm text-destructive">{{ error }}</p>
  </PanelCard>
</template>
