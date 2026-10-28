<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';
import Inbox from '~icons/nnz/inbox';

import { useActionRequired, useDismissActionRequired } from '@/api/actionRequired';
import { attentionWords } from '@/api/actionRequiredText';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  ResponsivePopover,
  ResponsivePopoverContent,
  ResponsivePopoverTrigger,
} from '@/components/ui/responsive-popover';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { type SaveError, useSaveErrorsStore } from '@/stores/saveErrors';

import { pageLabel } from './pageLabels';

const { t, te, locale } = useI18n();
const { channelId } = useCurrentChannel();
const { data: items } = useActionRequired(channelId);
const { mutate: dismissItem } = useDismissActionRequired(channelId);
const saveErrors = useSaveErrorsStore();
const open = ref(false);
const count = computed(() => (items.value?.length ?? 0) + saveErrors.items.length);
const router = useRouter();

// The page a failed save came from, by its sidebar name; a page outside the sidebar keeps its address.
function pageOf(failed: SaveError): string {
  const label = pageLabel(router?.resolve(failed.where).name);

  return label
    ? t(`frame.nav.${label}`)
    : failed.where;
}

function titleOf(failed: SaveError): string {
  return failed.what
    ? t('frame.inbox.notSaved', { what: t(`frame.inbox.what.${failed.what.key}`, failed.what.params ?? {}) })
    : t(failed.titleKey);
}

const attention = computed(() =>
  (items.value ?? []).map((item) => ({ id: item.id, ...attentionWords(item, {
    t,
    te,
    locale,
  }) })),
);
</script>

<template>
  <ResponsivePopover v-model:open="open">
    <ResponsivePopoverTrigger>
      <Button variant="outline" :size="count ? 'default' : 'icon'" data-error-inbox :aria-label="t('frame.inbox.title')">
        <Inbox class="size-5" aria-hidden="true" />
        <Badge v-if="count" variant="secondary">{{ count }}</Badge>
      </Button>
    </ResponsivePopoverTrigger>
    <ResponsivePopoverContent align="end" class="w-80 p-0" :title="t('frame.inbox.title')">
      <h2 class="border-b border-border px-4 py-3 text-sm font-semibold">
        {{ t('frame.inbox.title') }}
      </h2>
      <p v-if="!count" class="px-4 py-6 text-sm text-muted-foreground">
        {{ t('frame.inbox.empty') }}
      </p>
      <ul v-else class="flex flex-col p-1 md:max-h-80 md:overflow-y-auto">
        <li
          v-for="failed in saveErrors.items"
          :key="failed.id"
          class="flex flex-col gap-1 rounded-md px-3 py-2"
        >
          <span class="flex items-center gap-2">
            <span class="text-sm font-medium" data-save-what>{{ titleOf(failed) }}</span>
            <Badge v-if="failed.count > 1" variant="secondary" data-save-count>{{ failed.count }}</Badge>
          </span>
          <span class="text-sm text-muted-foreground" data-save-where>{{
            t('frame.inbox.on', { page: pageOf(failed) })
          }}</span>
          <span class="text-sm text-muted-foreground">{{ t(failed.messageKey) }}</span>
          <div class="mt-1 flex items-center gap-1 self-start">
            <Button variant="outline" size="sm" as-child>
              <RouterLink :to="failed.where" @click="open = false">{{
                t('frame.inbox.open')
              }}</RouterLink>
            </Button>
            <Button variant="ghost" size="sm" @click="saveErrors.dismiss(failed.id)">
              {{ t('frame.inbox.dismiss') }}
            </Button>
          </div>
        </li>
        <li
          v-for="item in attention"
          :key="item.id"
          class="flex flex-col gap-1 rounded-md px-3 py-2"
        >
          <span class="text-sm font-medium">{{ item.title }}</span>
          <span v-if="item.message" class="text-sm text-muted-foreground">{{ item.message }}</span>
          <div class="mt-1 flex items-center gap-1 self-start">
            <Button v-if="item.route" variant="outline" size="sm" as-child>
              <RouterLink :to="item.route" @click="open = false">{{
                item.open ?? t('frame.inbox.open')
              }}</RouterLink>
            </Button>
            <Button variant="ghost" size="sm" @click="dismissItem(item.id)">
              {{ t('frame.inbox.dismiss') }}
            </Button>
          </div>
        </li>
      </ul>
    </ResponsivePopoverContent>
  </ResponsivePopover>
</template>
