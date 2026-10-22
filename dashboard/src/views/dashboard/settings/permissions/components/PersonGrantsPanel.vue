<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Dangerous capabilities are given to one person, never to a whole role. Only the actions the bot allows
// as a personal grant are offered.
import Add from '~icons/nnz/add';
import Trash from '~icons/nnz/trash';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import type { ViewerOption } from '@/api/community';
import {
  useActionMatrix,
  useGrantCapability,
  usePersonGrants,
  namedGrantSlug,
  useRevokeCapability,
} from '@/api/permissions';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { errorText } from '@/lib/errors';

import { areaOf, useActionName, useAreaLabel } from '../actionArea';
import { isAllowedGrant } from '../personGrant';
import OptionSelect, { type Option } from './OptionSelect.vue';
import PersonPicker from './PersonPicker.vue';

defineProps<{ canManage: boolean }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const grants = usePersonGrants(channelId);
const matrix = useActionMatrix(channelId);
const grant = useGrantCapability(channelId);
const revoke = useRevokeCapability(channelId);
const state = usePanelState(grants);

const picked = ref<ViewerOption | null>(null);
const capability = ref<string | undefined>(undefined);
const error = ref<string | undefined>(undefined);

const rows = computed(() => (grants.data.value ?? []).filter(isAllowedGrant));

const knownText = (key: string, part: string): string =>
  t(`dashboard.settings.permissions.grants.known.${namedGrantSlug(key)}.${part}`);

const actionName = useActionName();
const descriptionOf = (key: string): string | null | undefined =>
  matrix.data.value?.find((row) => row.actionKey === key)?.description;

// A sensitive grant shows its own name; any other the plain name of its action, never the key.
const nameOf = (key: string): string =>
  namedGrantSlug(key) === undefined
    ? actionName({ actionKey: key, description: descriptionOf(key) })
    : knownText(key, 'name');

// The capabilities read by type: one section per area, the sections and the names inside them in name order.
const areaLabel = useAreaLabel();
const options = computed<Option[]>(() =>
  (matrix.data.value ?? [])
    .filter((row) => row.isGrantableViaPermit)
    .map((row) => ({
      value: row.actionKey,
      label: nameOf(row.actionKey),
      group: areaLabel(areaOf(row.actionKey)),
    }))
    .sort((left, right) =>
      left.group.localeCompare(right.group) || left.label.localeCompare(right.label)),
);

// The picked capability, when it is one that carries a consequence text.
const picking = computed(() => {
  const key = capability.value;

  return key !== undefined && namedGrantSlug(key) !== undefined
    ? key
    : undefined;
});

const confirming = ref(false);

const consequences = computed<string[]>(() =>
  picking.value === undefined
    ? []
    : [knownText(picking.value, 'can'), knownText(picking.value, 'cannot')],
);

const confirmTitle = computed<string>(() =>
  picking.value === undefined || picked.value === null
    ? ''
    : t('dashboard.settings.permissions.grants.confirmTitle', {
        name: picked.value.label,
        capability: nameOf(picking.value),
      }),
);

async function run(action: Promise<void>): Promise<void> {
  error.value = undefined;

  try {
    await action;
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}

async function give(): Promise<void> {
  if (picked.value === null || capability.value === undefined) return;

  await run(grant.mutateAsync({ userId: picked.value.id, permission: capability.value }));
  picked.value = null;
  confirming.value = false;
}

// A grant with a known consequence is confirmed first; the rest are given straight away.
async function add(): Promise<void> {
  if (picking.value === undefined)
    return give();

  confirming.value = true;
}
</script>

<template>
  <PanelCard
    data-panel="person-grants"
    :title="$t('dashboard.settings.permissions.grants.title')"
    :description="$t('dashboard.settings.permissions.grants.description')"
    :state="state"
    :error-text="$t('dashboard.settings.permissions.grants.loadFailed')"
    @retry="grants.refetch()"
  >
    <p v-if="rows.length === 0" class="pb-3 text-sm text-muted-foreground">
      {{ $t("dashboard.settings.permissions.grants.empty") }}
    </p>
    <RowGroup>
      <SummaryRow
        v-for="row in rows"
        :key="row.id"
        :data-grant="`${row.subjectId}:${row.resourceId}`"
        :title="row.subjectName ?? row.subjectId"
        :detail="row.resourceId == null ? undefined : nameOf(row.resourceId)"
      >
        <template #end>
          <Button
            v-if="canManage"
            variant="ghost"
            size="icon"
            data-grant-revoke
            :aria-label="
              $t('dashboard.settings.permissions.grants.revoke', {
                name: row.subjectName ?? row.subjectId,
              })
            "
            @click="
              run(revoke.mutateAsync({ userId: row.subjectId, permission: row.resourceId ?? '' }))
            "
          >
            <Trash class="size-5" />
          </Button>
        </template>
      </SummaryRow>
    </RowGroup>
    <div v-if="canManage" class="flex flex-wrap items-center gap-2 pt-3">
      <PersonPicker
        :picked="picked"
        :label="$t('dashboard.settings.permissions.grants.person')"
        @pick="picked = $event"
      />
      <OptionSelect
        v-model="capability"
        data-new-capability
        :label="$t('dashboard.settings.permissions.grants.capability')"
        :placeholder="$t('dashboard.settings.permissions.grants.capability')"
        :options="options"
      />
      <Button
        variant="outline"
        data-grant-add
        :disabled="picked === null || capability === undefined || grant.isPending.value"
        @click="add"
      >
        <Add class="size-5" />
        {{ $t("dashboard.settings.permissions.grants.add") }}
      </Button>
    </div>
    <p v-if="picking && canManage" data-grant-hint class="pt-3 text-sm text-muted-foreground">
      {{ knownText(picking, "detail") }}
    </p>
    <p v-if="error" role="alert" class="pt-3 text-sm text-destructive">{{ error }}</p>
  </PanelCard>
  <ConfirmDialog
    v-model:open="confirming"
    :title="confirmTitle"
    :description="picking === undefined ? undefined : knownText(picking, 'detail')"
    :consequences="consequences"
    :action="$t('dashboard.settings.permissions.grants.confirm')"
    :pending="grant.isPending.value"
    @confirm="give"
  />
</template>
