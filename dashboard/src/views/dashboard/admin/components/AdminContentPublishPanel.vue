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
// Publishing a version in three steps: pick a mode, preview how many channels it touches, then confirm.
// Confirm stays off until a preview of the chosen mode shows, and it sends that preview's count back so the
// server refuses a publish whose numbers moved in the meantime. The job it starts is read until it ends.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  PUBLISH_MODES,
  usePublish,
  usePublishJob,
  usePublishPreview,
  type PublishMode,
} from '@/api/adminContent';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldLabel, FieldTitle } from '@/components/ui/field';
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { lookup } from '@/lib/array';
import { errorText } from '@/lib/errors';

const props = defineProps<{
  definitionId: string;
  versionId: string;
}>();

const statusTones = {
  running: 'attention',
  completed: 'ok',
  failed: 'bad',
} as const satisfies Record<string, StatusTone>;

const { t } = useI18n();
const { can } = usePlatformCan();

const mode = ref<PublishMode>('publish_as_new');
const jobId = ref<string | null>(null);

const preview = usePublishPreview();
const publish = usePublish();
const job = usePublishJob(jobId);

// Force can overwrite a channel's own edits, so it is offered only to an operator who holds its own key.
const modes = computed(() =>
  PUBLISH_MODES.filter((value) => value !== 'force' || can('content:publish:force')));
const forcing = computed(() => mode.value === 'force');
const previewed = computed(() => preview.data.value ?? null);
const affected = computed(() => Number(previewed.value?.affectedCount ?? 0));
const shownJob = computed(() => job.data.value ?? publish.data.value ?? null);
const failure = computed(() => preview.error.value ?? publish.error.value ?? job.error.value);

const jobTone = computed<StatusTone>(() => lookup(statusTones, shownJob.value?.status) ?? 'off');

// Copies the publish could not update, by what they are; a kind with none is not listed.
const failedCopies = computed(() => {
  const current = shownJob.value;

  if (!current)
    return [];

  return [
    { kind: 'widgets', count: current.rebuildFailedWidgetIds.length },
    { kind: 'pipelines', count: current.validationFailedPipelineIds.length },
    { kind: 'scripts', count: current.validationFailedCodeScriptIds.length },
    { kind: 'templates', count: current.updateFailedTemplateRowIds.length },
  ].filter((row) => row.count > 0);
});

// A preview belongs to one version and one mode; any other choice needs its own count.
watch([() => props.definitionId, () => props.versionId, mode], () => {
  preview.reset();
  publish.reset();
  jobId.value = null;
});

function runPreview(): void {
  preview.mutate({
    definitionId: props.definitionId,
    versionId: props.versionId,
    mode: mode.value,
  });
}

function confirm(): void {
  publish.mutate(
    {
      definitionId: props.definitionId,
      versionId: props.versionId,
      mode: mode.value,
      confirmedCount: affected.value,
    },
    { onSuccess: (started) => {
      jobId.value = started.id;
    } },
  );
}
</script>

<template>
  <section class="grid gap-3" data-publish-panel>
    <Field>
      <FieldTitle>{{ $t("dashboard.admin.content.publish.mode") }}</FieldTitle>
      <RadioGroup v-model="mode" class="grid gap-2">
        <div v-for="value in modes" :key="value" class="flex items-start gap-2">
          <RadioGroupItem
            :id="`content-publish-${value}`"
            :value="value"
            :data-publish-mode="value"
            class="mt-0.5"
          />
          <FieldLabel :for="`content-publish-${value}`" class="grid gap-0.5 font-normal">
            <span class="font-medium">{{ $t(`dashboard.admin.content.publish.modes.${value}.label`) }}</span>
            <span class="text-xs text-muted-foreground">{{
              $t(`dashboard.admin.content.publish.modes.${value}.hint`)
            }}</span>
          </FieldLabel>
        </div>
      </RadioGroup>
    </Field>

    <InlineNotice v-if="forcing" tone="error" data-publish-force-warning>
      {{ $t("dashboard.admin.content.publish.forceWarning") }}
    </InlineNotice>

    <div v-if="previewed" class="grid gap-1 text-sm" data-publish-count>
      <p class="m-0 flex flex-wrap gap-x-3">
        <span class="font-medium">{{
          $t("dashboard.admin.content.publish.affected", { count: affected }, affected)
        }}</span>
        <span class="text-muted-foreground">{{
          $t("dashboard.admin.content.publish.skipped", { count: Number(previewed.skippedCount) })
        }}</span>
      </p>
      <p v-if="previewed.sampleTenantNames.length > 0" class="m-0 text-xs text-muted-foreground">
        {{
          $t("dashboard.admin.content.publish.samples", { names: previewed.sampleTenantNames.join(", ") })
        }}
      </p>
    </div>

    <InlineNotice v-if="failure" tone="error" compact>
      {{ errorText(failure, t) }}
    </InlineNotice>

    <div class="flex justify-end gap-2">
      <Button
        variant="outline"
        data-publish-preview
        :disabled="preview.isPending.value"
        @click="runPreview"
      >{{ $t("dashboard.admin.content.publish.preview") }}</Button>
      <Button
        :variant="forcing
          ? 'destructive'
          : 'default'"
        data-publish-confirm
        :disabled="previewed === null || publish.isPending.value"
        @click="confirm"
      >{{
        forcing
          ? $t("dashboard.admin.content.publish.confirmForce")
          : $t("dashboard.admin.content.publish.confirm")
      }}</Button>
    </div>

    <div v-if="shownJob" class="grid gap-1 text-sm" :data-status="shownJob.status" data-publish-job>
      <StatusBadge
        :tone="jobTone"
        :label="$t(`dashboard.admin.content.publish.status.${shownJob.status}`)"
      />
      <p v-if="shownJob.status === 'completed'" class="m-0 text-muted-foreground">
        {{
          $t(
            "dashboard.admin.content.publish.updated",
            { count: Number(shownJob.confirmedAffectedCount ?? 0) },
            Number(shownJob.confirmedAffectedCount ?? 0),
          )
        }}
      </p>
      <p v-if="shownJob.failureReason" class="m-0 text-destructive">{{ shownJob.failureReason }}</p>
      <p
        v-for="row in failedCopies"
        :key="row.kind"
        class="m-0 text-destructive"
        data-publish-failed
      >
        {{ $t(`dashboard.admin.content.publish.failed.${row.kind}`, { count: row.count }, row.count) }}
      </p>
    </div>
  </section>
</template>
