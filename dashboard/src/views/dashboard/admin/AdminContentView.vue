<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot. You may use, change and share it for free,
   for any noncommercial purpose, under the PolyForm Noncommercial License
   1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
   See LICENSE for details, and TRADEMARKS.md for the name and logo.

   SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// The platform content catalog: the definitions every channel can install, their version history and
// how many channel copies exist. An operator creates a definition, edits a draft in the JSON editor, publishes
// a version through preview and confirm, or retires a definition. Other operators' changes arrive over the
// admin hub.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  CONTENT_KINDS,
  useContentDefinition,
  useContentDefinitions,
  useContentVersion,
  useCreateContentDefinition,
  useDraftContentVersion,
  useRetireContentDefinition,
  type ContentDefinition,
} from '@/api/adminContent';
import FormDialog from '@/components/layout/FormDialog.vue';
import OptionSelect from '@/components/layout/OptionSelect.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';

import AdminAudioAssetsPanel from './components/AdminAudioAssetsPanel.vue';
import AdminContentPayloadEditor from './components/AdminContentPayloadEditor.vue';
import AdminContentPublishPanel from './components/AdminContentPublishPanel.vue';

type Step = 'create' | 'retire';
type EditorTarget = 'create' | 'draft';

const { t } = useI18n();
const { can } = usePlatformCan();

const kindFilter = ref('');
const pickedId = ref<string | null>(null);
const pickedVersionId = ref<string | null>(null);
const editing = ref<EditorTarget | null>(null);
const editorSeed = ref('{}');
const step = ref<Step | null>(null);
const failure = ref<string | null>(null);
const kind = ref('');
const key = ref('');
const displayName = ref('');
const description = ref('');
const payload = ref('');

const definitions = useContentDefinitions(kindFilter);
const detail = useContentDefinition(pickedId);
const version = useContentVersion(pickedId, pickedVersionId);
const create = useCreateContentDefinition();
const draft = useDraftContentVersion();
const retire = useRetireContentDefinition();

const listState = usePanelState(definitions, (rows) =>
  rows.length === 0);
const detailState = usePanelState(detail, (row) =>
  row === null);

const picked = computed(() => detail.data.value);
const installs = computed(() => picked.value?.installs);
const pickedVersion = computed(() => version.data.value ?? null);
const publishable = computed(
  () => pickedVersion.value !== null && !pickedVersion.value.publishedAt,
);
const pending = computed(() => create.isPending.value || retire.isPending.value);

const editorTitle = computed<string>(() =>
  editing.value === 'create'
    ? t('dashboard.admin.content.create.title')
    : t('dashboard.admin.content.draft.title'));

const kindChoices = computed(() =>
  CONTENT_KINDS.map((value) => ({
    value,
    label: t(`dashboard.admin.content.kinds.${value}`),
  })));

// A draft belongs to the picked definition; a new definition has the kind the operator chose.
const editorKind = computed<string>(() =>
  editing.value === 'create'
    ? kind.value
    : (picked.value?.definition.kind ?? ''));

const titleKeys = {
  create: 'create.title',
  retire: 'retire.title',
} as const satisfies Record<Step, string>;

const actionKeys = {
  create: 'create.action',
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

// A version belongs to one definition: another definition starts with no version picked.
watch(pickedId, () => {
  pickedVersionId.value = null;
  editing.value = null;
});

function pick(row: ContentDefinition): void {
  pickedId.value = row.id;
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

// A new pipeline starts as an empty step list, the shape its step editor reads.
function openCreateEditor(): void {
  editorSeed.value = kind.value === 'pipeline' && payload.value === '{}'
    ? '{"steps":[]}'
    : payload.value;

  editing.value = 'create';
}

// The draft starts from the picked version; with none picked it starts from the current one.
function openDraft(): void {
  const current = picked.value;
  const currentId = current?.definition.currentVersionId;
  const latest = current?.versions.find((row) => row.id === currentId) ?? current?.versions[0];

  editorSeed.value = pickedVersion.value?.payloadJson ?? latest?.payloadJson ?? '{}';
  editing.value = 'draft';
}

async function keepPayload(payloadJson: string): Promise<string> {
  if (editing.value === 'create') {
    payload.value = payloadJson;

    return t('dashboard.admin.content.create.kept');
  }

  try {
    await draft.mutateAsync({ definitionId: pickedId.value!, payloadJson });

    return t('dashboard.admin.content.draft.saved');
  } catch {
    return t('dashboard.admin.content.failed');
  }
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

  return retire.mutateAsync(pickedId.value!);
}

async function submit(): Promise<void> {
  const current = step.value;

  if (current === null) return;

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
  <PageBody data-page-body>
    <Field data-page-inset>
      <FieldLabel for="content-kind">{{ $t("dashboard.admin.content.kindFilter") }}</FieldLabel>
      <Input
        id="content-kind"
        v-model="kindFilter"
        class="max-w-xs!"
        data-kind-filter
        :placeholder="$t('dashboard.admin.content.kindPlaceholder')"
      />
    </Field>

    <PanelCard
      tier="lead"
      :title="$t('dashboard.admin.content.list.title')"
      :description="$t('dashboard.admin.content.list.description')"
      :state="listState"
      :empty-text="$t('dashboard.admin.content.list.empty')"
      :error-text="$t('dashboard.admin.content.error')"
      @retry="definitions.refetch()"
    >
      <RowGroup>
        <SummaryRow
          v-for="row in definitions.data.value ?? []"
          :key="row.id"
          data-definition-row
          pick
          truncate
          :selected="row.id === pickedId"
          :title="row.displayName"
          :detail="$t('dashboard.admin.content.kindKey', { kind: row.kind, key: row.key })"
          @click="pick(row)"
        >
          <template #end>
            <StatusBadge
              v-if="row.retiredAt"
              tone="off"
              :label="$t('dashboard.admin.content.retired')"
            />
            <span v-else class="shrink-0 text-xs text-muted-foreground">
              {{ $t("dashboard.admin.content.version", { version: Number(row.currentVersion ?? 0) }) }}
            </span>
          </template>
        </SummaryRow>
      </RowGroup>
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
      <RowGroup>
        <SummaryRow
          v-for="row in picked?.versions ?? []"
          :key="row.id"
          data-version-row
          pick
          truncate
          :selected="row.id === pickedVersionId"
          :title="$t('dashboard.admin.content.version', { version: Number(row.version) })"
          @click="pickedVersionId = row.id"
        >
          <template #meta>
            <span class="block truncate font-mono">{{ row.payloadJson }}</span>
          </template>
          <template #end>
            <Badge variant="outline">{{
              row.publishedAt
                ? $t("dashboard.admin.content.published")
                : $t("dashboard.admin.content.draftState")
            }}</Badge>
          </template>
        </SummaryRow>
      </RowGroup>
      <AdminContentPublishPanel
        v-if="publishable && pickedId && pickedVersionId && can('content:publish')"
        class="mt-4"
        :definition-id="pickedId"
        :version-id="pickedVersionId"
      />
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

    <AdminAudioAssetsPanel />

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
        <Field>
          <FieldLabel for="content-new-kind">{{ $t("dashboard.admin.content.fields.kind") }}</FieldLabel>
          <OptionSelect
            id="content-new-kind"
            v-model="kind"
            data-field-kind
            :options="kindChoices"
            :placeholder="$t('dashboard.admin.content.kindChoose')"
          />
        </Field>
        <Field>
          <FieldLabel for="content-new-key">{{ $t("dashboard.admin.content.fields.key") }}</FieldLabel>
          <Input id="content-new-key" v-model="key" data-field-key />
        </Field>
        <Field>
          <FieldLabel for="content-new-name">{{ $t("dashboard.admin.content.fields.name") }}</FieldLabel>
          <Input id="content-new-name" v-model="displayName" data-field-name />
        </Field>
        <Field>
          <FieldLabel for="content-new-description">{{
            $t("dashboard.admin.content.fields.description")
          }}</FieldLabel>
          <Input id="content-new-description" v-model="description" data-field-description />
        </Field>
      </div>
      <Field v-if="step === 'create' && can('content:author')" class="mt-3">
        <FieldLabel for="content-payload">{{ $t("dashboard.admin.content.fields.payload") }}</FieldLabel>
        <Button
          id="content-payload"
          variant="outline"
          type="button"
          class="justify-start truncate font-mono text-xs"
          data-field-payload
          @click="openCreateEditor"
        >{{ payload }}</Button>
      </Field>
    </FormDialog>
    <AdminContentPayloadEditor
      v-if="editing && can('content:author')"
      :title="editorTitle"
      :kind="editorKind"
      :initial="editorSeed"
      :keep="keepPayload"
      @close="editing = null"
    />
  </PageBody>
</template>
