<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot, free software licensed under the GNU Affero
   General Public License v3.0 or later. You may redistribute and/or modify it
   under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

   SPDX-License-Identifier: AGPL-3.0-or-later
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// The platform content catalog: the definitions every channel can install, their version history and
// how many channel copies exist. An operator creates a definition, drafts a new version of it, or retires
// it. Other operators' changes arrive over the admin hub.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useContentDefinition,
  useContentDefinitions,
  useCreateContentDefinition,
  useDraftContentVersion,
  useRetireContentDefinition,
  type ContentDefinition,
} from '@/api/adminContent';
import FormDialog from '@/components/layout/FormDialog.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';

type Step = 'create' | 'draft' | 'retire';

const { t } = useI18n();
const { can } = usePlatformCan();

const kindFilter = ref('');
const pickedId = ref<string | null>(null);
const step = ref<Step | null>(null);
const failure = ref<string | null>(null);
const kind = ref('');
const key = ref('');
const displayName = ref('');
const description = ref('');
const payload = ref('');

const definitions = useContentDefinitions(kindFilter);
const detail = useContentDefinition(pickedId);
const create = useCreateContentDefinition();
const draft = useDraftContentVersion();
const retire = useRetireContentDefinition();

const listState = usePanelState(definitions, (rows) =>
  rows.length === 0);
const detailState = usePanelState(detail, (row) =>
  row === null);

const picked = computed(() => detail.data.value);
const installs = computed(() => picked.value?.installs);
const pending = computed(
  () => create.isPending.value || draft.isPending.value || retire.isPending.value,
);

const titleKeys = {
  create: 'create.title',
  draft: 'draft.title',
  retire: 'retire.title',
} as const satisfies Record<Step, string>;

const actionKeys = {
  create: 'create.action',
  draft: 'draft.action',
  retire: 'retire.action',
} as const satisfies Record<Step, string>;

const dialogTitle = computed<string>(() =>
  step.value
    ? t(`dashboard.admin.content.${titleKeys[step.value]}`)
    : '',
);

const dialogAction = computed<string>(() =>
  step.value
    ? t(`dashboard.admin.content.${actionKeys[step.value]}`)
    : '',
);

// The consequence is shown before the operator confirms, counted from the real installed copies.
const consequence = computed<string | undefined>(() => {
  if (step.value !== 'retire')
    return undefined;

  return t('dashboard.admin.content.retire.consequence', {
    name: picked.value?.definition.displayName ?? '',
    installed: Number(installs.value?.installedCount ?? 0),
    behind: Number(installs.value?.behindCount ?? 0),
    edited: Number(installs.value?.editedCount ?? 0),
  });
});

const error = computed<string | undefined>(() => failure.value ?? undefined);

function pick(row: ContentDefinition): void {
  pickedId.value = row.id;
}

function isJson(value: string): boolean {
  try {
    JSON.parse(value);

    return true;
  } catch {
    return false;
  }
}

function openCreate(): void {
  kind.value = '';
  key.value = '';
  displayName.value = '';
  description.value = '';
  payload.value = '{}';
  failure.value = null;
  step.value = 'create';
}

function openDraft(): void {
  const current = picked.value;
  const currentId = current?.definition.currentVersionId;
  const latest = current?.versions.find((row) => row.id === currentId) ?? current?.versions[0];

  payload.value = latest?.payloadJson ?? '{}';
  failure.value = null;
  step.value = 'draft';
}

function openRetire(): void {
  failure.value = null;
  step.value = 'retire';
}

function close(): void {
  step.value = null;
}

function run(current: Step): Promise<void> {
  if (current === 'create')
    return create.mutateAsync({
      kind: kind.value.trim(),
      key: key.value.trim(),
      displayName: displayName.value.trim(),
      description: description.value.trim() === ''
        ? null
        : description.value.trim(),
      payloadJson: payload.value,
    });

  if (current === 'draft')
    return draft.mutateAsync({ definitionId: pickedId.value!, payloadJson: payload.value });

  return retire.mutateAsync(pickedId.value!);
}

async function submit(): Promise<void> {
  const current = step.value;

  if (current === null) return;

  if (current !== 'retire' && !isJson(payload.value)) {
    failure.value = t('dashboard.admin.content.invalidPayload');

    return;
  }

  try {
    await run(current);
    step.value = null;
  } catch {
    failure.value = t('dashboard.admin.content.failed');
  }
}
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.content.title')" :crumb="$t('dashboard.admin.crumb')">
    <template #actions>
      <Button v-if="can('content:author')" @click="openCreate">{{ $t("dashboard.admin.content.create.open") }}</Button>
    </template>
  </PageHeader>
  <div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 p-6 max-md:p-4">
    <div class="grid gap-1.5">
      <Label for="content-kind">{{ $t("dashboard.admin.content.kindFilter") }}</Label>
      <Input
        id="content-kind"
        v-model="kindFilter"
        class="max-w-xs"
        data-kind-filter
        :placeholder="$t('dashboard.admin.content.kindPlaceholder')"
      />
    </div>

    <PanelCard
      tier="lead"
      :title="$t('dashboard.admin.content.list.title')"
      :description="$t('dashboard.admin.content.list.description')"
      :state="listState"
      :empty-text="$t('dashboard.admin.content.list.empty')"
      :error-text="$t('dashboard.admin.content.error')"
      @retry="definitions.refetch()"
    >
      <ul class="divide-y divide-border">
        <li
          v-for="row in definitions.data.value ?? []"
          :key="row.id"
          data-definition-row
          class="flex items-center gap-3 py-2"
        >
          <button
            type="button"
            class="min-w-0 flex-1 text-left"
            :class="{ 'font-medium': row.id === pickedId }"
            @click="pick(row)"
          >
            <span class="block truncate text-sm font-medium">{{ row.displayName }}</span>
            <span class="block truncate text-xs text-muted-foreground">
              {{ $t("dashboard.admin.content.kindKey", { kind: row.kind, key: row.key }) }}
            </span>
          </button>
          <Badge v-if="row.retiredAt" variant="outline">{{
            $t("dashboard.admin.content.retired")
          }}</Badge>
          <span v-else class="text-xs text-muted-foreground">
            {{ $t("dashboard.admin.content.version", { version: Number(row.currentVersion ?? 0) }) }}
          </span>
        </li>
      </ul>
    </PanelCard>

    <PanelCard
      v-if="pickedId"
      :title="picked?.definition.displayName ?? ''"
      :description="picked?.definition.description ?? undefined"
      :state="detailState"
      :empty-text="$t('dashboard.admin.content.detail.empty')"
      :error-text="$t('dashboard.admin.content.error')"
      @retry="detail.refetch()"
    >
      <p v-if="installs" class="mb-2 text-sm text-muted-foreground" data-installs>
        {{
          $t("dashboard.admin.content.detail.installs", {
            installed: Number(installs.installedCount),
            behind: Number(installs.behindCount),
            edited: Number(installs.editedCount),
          })
        }}
      </p>
      <ul class="divide-y divide-border">
        <li
          v-for="row in picked?.versions ?? []"
          :key="row.id"
          data-version-row
          class="flex items-center gap-3 py-2"
        >
          <span class="text-sm font-medium">
            {{ $t("dashboard.admin.content.version", { version: Number(row.version) }) }}
          </span>
          <span class="min-w-0 flex-1 truncate font-mono text-xs text-muted-foreground">
            {{ row.payloadJson }}
          </span>
          <Badge variant="outline">{{
            row.publishedAt
              ? $t("dashboard.admin.content.published")
              : $t("dashboard.admin.content.draftState")
          }}</Badge>
        </li>
      </ul>
      <template v-if="can('content:author')" #footer>
        <div class="flex w-full justify-end gap-2">
          <Button variant="outline" @click="openDraft">{{
            $t("dashboard.admin.content.draft.open")
          }}</Button>
          <Button
            variant="outline"
            :disabled="!!picked?.definition.retiredAt"
            @click="openRetire"
          >{{ $t("dashboard.admin.content.retire.open") }}</Button>
        </div>
      </template>
    </PanelCard>

    <FormDialog
      :open="step !== null"
      :title="dialogTitle"
      :description="consequence"
      :action="dialogAction"
      :pending="pending"
      :error="error"
      @update:open="close"
      @submit="submit"
    >
      <div v-if="step === 'create'" class="grid gap-3">
        <div class="grid gap-1.5">
          <Label for="content-new-kind">{{ $t("dashboard.admin.content.fields.kind") }}</Label>
          <Input id="content-new-kind" v-model="kind" data-field-kind />
        </div>
        <div class="grid gap-1.5">
          <Label for="content-new-key">{{ $t("dashboard.admin.content.fields.key") }}</Label>
          <Input id="content-new-key" v-model="key" data-field-key />
        </div>
        <div class="grid gap-1.5">
          <Label for="content-new-name">{{ $t("dashboard.admin.content.fields.name") }}</Label>
          <Input id="content-new-name" v-model="displayName" data-field-name />
        </div>
        <div class="grid gap-1.5">
          <Label for="content-new-description">{{
            $t("dashboard.admin.content.fields.description")
          }}</Label>
          <Input id="content-new-description" v-model="description" data-field-description />
        </div>
      </div>
      <div v-if="step === 'create' || step === 'draft'" class="mt-3 grid gap-1.5">
        <Label for="content-payload">{{ $t("dashboard.admin.content.fields.payload") }}</Label>
        <Textarea
          id="content-payload"
          v-model="payload"
          class="min-h-40 font-mono text-xs"
          data-field-payload
        />
      </div>
    </FormDialog>
  </div>
</template>
