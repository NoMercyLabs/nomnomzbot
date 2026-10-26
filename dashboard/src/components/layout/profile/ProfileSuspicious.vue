<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Twitch's suspicious-user flag for one person: watch them, hold their messages for review, or clear the flag.
// Twitch gives no way to read the flag back for one person, so the section shows what Twitch answered after
// the last change this session and says nothing before that. All three buttons are outline: none is the
// primary action of the page.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useChannelAccess } from '@/api/access';
import {
  SUSPICIOUS_STATUSES,
  useSuspiciousChange,
  type SuspiciousStatus,
  type SuspiciousStatusKind,
} from '@/api/moderation';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const props = defineProps<{ userId: string; name: string }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);
const change = useSuspiciousChange(channelId);

const canWrite = computed<boolean>(() => access.can('moderation:suspicioususer:write'));
const answered = ref<SuspiciousStatus | null | undefined>(undefined);

const statusText = computed<string | null>(() => {
  if (answered.value === undefined)
    return null;

  return answered.value === null || answered.value.status === 'none'
    ? t('profile.suspicious.none')
    : t(`profile.suspicious.status.${answered.value.status}`);
});

function apply(status: SuspiciousStatusKind | null): void {
  change.mutate(
    { userId: props.userId, status },
    {
      onSuccess: (result: SuspiciousStatus | null) => {
        answered.value = result;
        toast.success(t('profile.suspicious.done', { name: props.name }));
      },
      onError: () => toast.error(t('profile.suspicious.failed', { name: props.name })),
    },
  );
}
</script>

<template>
  <section v-if="canWrite" data-profile-suspicious class="grid gap-3">
    <h3 class="text-sm font-semibold">{{ $t("profile.suspicious.title") }}</h3>
    <p class="text-sm text-muted-foreground">{{ $t("profile.suspicious.explain") }}</p>
    <p v-if="statusText" data-suspicious-status class="text-sm font-medium">{{ statusText }}</p>
    <div class="flex flex-wrap gap-2">
      <Button
        v-for="status in SUSPICIOUS_STATUSES"
        :key="status"
        type="button"
        variant="outline"
        size="sm"
        :data-suspicious-set="status"
        :disabled="change.isPending.value"
        @click="apply(status)"
      >
        {{ $t(`profile.suspicious.set.${status}`) }}
      </Button>
      <Button
        type="button"
        variant="outline"
        size="sm"
        data-suspicious-clear
        :disabled="change.isPending.value"
        @click="apply(null)"
      >
        {{ $t("profile.suspicious.clear") }}
      </Button>
    </div>
  </section>
</template>
