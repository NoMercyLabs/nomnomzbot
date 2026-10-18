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
// The platform trust and safety desk: review the automatic account actions, see accounts flagged across
// channels, and apply or lift a network-wide block. The bot audits every call with the operator's reason,
// so nothing loads or changes until one is stated. Other operators' changes arrive over the admin hub.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useApplyNetworkBlock,
  useBlockPreview,
  useCrossTenantSignals,
  useLiftNetworkBlock,
  useNetworkBlocks,
  useReviewDecision,
  useReviewQueue,
  type NetworkBlock,
  type PreviewParams,
  type TrustSafetyReviewItem,
} from '@/api/adminTrustSafety';
import FormDialog from '@/components/layout/FormDialog.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

type Step =
  | { kind: 'confirm' | 'overturn'; item: TrustSafetyReviewItem } |
  { kind: 'lift'; block: NetworkBlock } |
  { kind: 'apply' };

const { t } = useI18n();
const { can } = usePlatformCan();

const justification = ref('');
const targetId = ref('');
const reason = ref('');
const previewParams = ref<PreviewParams | null>(null);
const step = ref<Step | null>(null);
const failed = ref(false);

const queue = useReviewQueue(justification);
const signals = useCrossTenantSignals(justification);
const blocks = useNetworkBlocks(justification);
const preview = useBlockPreview(previewParams);

const confirmReview = useReviewDecision('confirm');
const overturnReview = useReviewDecision('overturn');
const lift = useLiftNetworkBlock();
const apply = useApplyNetworkBlock();

const queueState = usePanelState(queue, (rows) =>
  rows.length === 0);
const signalState = usePanelState(signals, (rows) =>
  rows.length === 0);
const blockState = usePanelState(blocks, (rows) =>
  rows.length === 0);

const previewCount = computed(() => Number(preview.data.value?.tenantCount ?? 0));
const pending = computed(
  () =>
    confirmReview.isPending.value ||
    overturnReview.isPending.value ||
    lift.isPending.value ||
    apply.isPending.value,
);

const titleKeys = {
  confirm: 'review.confirmTitle',
  overturn: 'review.overturnTitle',
  lift: 'blocks.liftTitle',
  apply: 'apply.dialogTitle',
} as const satisfies Record<Step['kind'], string>;

const actionKeys = {
  confirm: 'review.confirm',
  overturn: 'review.overturn',
  lift: 'blocks.liftAction',
  apply: 'apply.action',
} as const satisfies Record<Step['kind'], string>;

const dialogTitle = computed<string>(() =>
  step.value
    ? t(`dashboard.admin.trustSafety.${titleKeys[step.value.kind]}`)
    : '',
);

const dialogAction = computed<string>(() =>
  step.value
    ? t(`dashboard.admin.trustSafety.${actionKeys[step.value.kind]}`)
    : '',
);

// The consequence is shown before the operator confirms, built from what the bot says it will touch.
const consequence = computed<string>(() => {
  const current = step.value;

  if (current?.kind === 'overturn')
    return t('dashboard.admin.trustSafety.review.overturnConsequence', {
      preview: current.item.reversalPreview,
    });

  if (current?.kind === 'confirm')
    return t('dashboard.admin.trustSafety.review.confirmConsequence');

  if (current?.kind === 'lift')
    return t('dashboard.admin.trustSafety.blocks.liftConsequence', {
      count: Number(current.block.channelCount),
    });

  return t('dashboard.admin.trustSafety.apply.consequence', { count: previewCount.value });
});

function startPreview(): void {
  previewParams.value = {
    targetTwitchUserId: targetId.value.trim(),
    justification: justification.value,
  };
}

function close(): void {
  step.value = null;
  failed.value = false;
}

async function submit(): Promise<void> {
  const current = step.value;

  if (!current) return;

  try {
    await run(current);
    close();
  } catch {
    failed.value = true;
  }
}

function run(current: Step): Promise<void> {
  const reason_ = justification.value;

  if (current.kind === 'confirm')
    return confirmReview.mutateAsync({
      detectionId: current.item.detectionId,
      justification: reason_,
    });

  if (current.kind === 'overturn')
    return overturnReview.mutateAsync({
      detectionId: current.item.detectionId,
      justification: reason_,
    });

  if (current.kind === 'lift')
    return lift.mutateAsync({ blockId: current.block.id, justification: reason_ });

  return apply.mutateAsync({
    targetTwitchUserId: targetId.value.trim(),
    reason: reason.value,
    justification: reason_,
    confirmedTenantCount: previewCount.value,
  });
}
</script>

<template>
  <PageHeader
    :title="$t('dashboard.admin.trustSafety.title')"
    :crumb="$t('dashboard.admin.crumb')"
  />
  <div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 p-6 max-md:p-4">
    <div class="grid gap-1.5">
      <Label for="ts-justification">{{
        $t("dashboard.admin.trustSafety.justification.label")
      }}</Label>
      <Input
        id="ts-justification"
        v-model="justification"
        data-justification
        :placeholder="$t('dashboard.admin.trustSafety.justification.placeholder')"
      />
      <p class="text-xs text-muted-foreground">
        {{ $t("dashboard.admin.trustSafety.justification.hint") }}
      </p>
    </div>

    <PanelCard
      tier="lead"
      :title="$t('dashboard.admin.trustSafety.review.title')"
      :description="$t('dashboard.admin.trustSafety.review.description')"
      :state="justification.trim() ? queueState : 'empty'"
      :empty-text="
        justification.trim()
          ? $t('dashboard.admin.trustSafety.review.empty')
          : $t('dashboard.admin.trustSafety.idle')
      "
      :error-text="$t('dashboard.admin.trustSafety.error')"
      @retry="queue.refetch()"
    >
      <ul class="divide-y divide-border">
        <li
          v-for="item in queue.data.value ?? []"
          :key="item.detectionId"
          data-review-row
          class="flex items-center gap-3 py-2"
        >
          <div class="min-w-0 flex-1">
            <p class="truncate text-sm font-medium">
              {{ item.subjectDisplayName }}<span class="text-muted-foreground"> {{ item.channelName }}</span>
            </p>
            <p class="truncate text-xs text-muted-foreground">{{ item.reason }}</p>
          </div>
          <Button
            v-if="can('trust-safety:review')"
            variant="outline"
            size="sm"
            @click="step = { kind: 'confirm', item }"
          >{{
            $t("dashboard.admin.trustSafety.review.confirm")
          }}</Button>
          <Button
            v-if="can('trust-safety:review')"
            variant="outline"
            size="sm"
            @click="step = { kind: 'overturn', item }"
          >{{
            $t("dashboard.admin.trustSafety.review.overturn")
          }}</Button>
        </li>
      </ul>
    </PanelCard>

    <PanelCard
      :title="$t('dashboard.admin.trustSafety.signals.title')"
      :description="$t('dashboard.admin.trustSafety.signals.description')"
      :state="justification.trim() ? signalState : 'empty'"
      :empty-text="
        justification.trim()
          ? $t('dashboard.admin.trustSafety.signals.empty')
          : $t('dashboard.admin.trustSafety.idle')
      "
      :error-text="$t('dashboard.admin.trustSafety.error')"
      @retry="signals.refetch()"
    >
      <ul class="divide-y divide-border">
        <li
          v-for="signal in signals.data.value ?? []"
          :key="signal.provider + signal.subjectPlatformUserId"
          data-signal-row
          class="py-2"
        >
          <p class="text-sm font-medium">{{ signal.subjectDisplayName }}</p>
          <p class="text-xs text-muted-foreground">
            {{
              $t("dashboard.admin.trustSafety.signals.summary", {
                tenants: Number(signal.tenantCount),
                detections: Number(signal.detectionCount),
              })
            }}
          </p>
        </li>
      </ul>
    </PanelCard>

    <PanelCard
      :title="$t('dashboard.admin.trustSafety.blocks.title')"
      :description="$t('dashboard.admin.trustSafety.blocks.description')"
      :state="justification.trim() ? blockState : 'empty'"
      :empty-text="
        justification.trim()
          ? $t('dashboard.admin.trustSafety.blocks.empty')
          : $t('dashboard.admin.trustSafety.idle')
      "
      :error-text="$t('dashboard.admin.trustSafety.error')"
      @retry="blocks.refetch()"
    >
      <ul class="divide-y divide-border">
        <li
          v-for="block in blocks.data.value ?? []"
          :key="block.id"
          data-block-row
          class="flex items-center gap-3 py-2"
        >
          <div class="min-w-0 flex-1">
            <p class="truncate text-sm font-medium">
              {{ block.targetDisplayName ?? block.targetTwitchUserId }}
            </p>
            <p class="text-xs text-muted-foreground">
              {{
                $t("dashboard.admin.trustSafety.blocks.summary", {
                  status: block.status,
                  count: Number(block.channelCount),
                })
              }}
            </p>
          </div>
          <Button
            v-if="can('network:block:manage')"
            variant="outline"
            size="sm"
            @click="step = { kind: 'lift', block }"
          >{{
            $t("dashboard.admin.trustSafety.blocks.lift")
          }}</Button>
        </li>
      </ul>
      <template v-if="can('network:block:manage')" #footer>
        <div class="grid w-full gap-2">
          <p class="text-sm font-medium">{{ $t("dashboard.admin.trustSafety.apply.title") }}</p>
          <div class="flex flex-wrap gap-2">
            <Input
              v-model="targetId"
              data-block-target
              class="w-48"
              :placeholder="$t('dashboard.admin.trustSafety.apply.target')"
            />
            <Input
              v-model="reason"
              class="min-w-0 flex-1"
              :placeholder="$t('dashboard.admin.trustSafety.apply.reason')"
            />
            <Button
              variant="outline"
              :disabled="!justification.trim() || !targetId.trim()"
              @click="startPreview"
            >
              {{ $t("dashboard.admin.trustSafety.apply.preview") }}
            </Button>
            <Button
              v-if="preview.data.value"
              variant="destructive"
              @click="step = { kind: 'apply' }"
            >
              {{ $t("dashboard.admin.trustSafety.apply.action") }}
            </Button>
          </div>
          <p v-if="preview.data.value" class="text-xs text-muted-foreground">
            {{
              $t("dashboard.admin.trustSafety.apply.previewResult", {
                name: preview.data.value.targetDisplayName ?? targetId,
                count: previewCount,
              })
            }}
          </p>
        </div>
      </template>
    </PanelCard>

    <FormDialog
      :open="step !== null"
      :title="dialogTitle"
      :description="consequence"
      :action="dialogAction"
      :pending="pending"
      :error="failed ? $t('dashboard.admin.trustSafety.failed') : undefined"
      @update:open="close"
      @submit="submit"
    />
  </div>
</template>
