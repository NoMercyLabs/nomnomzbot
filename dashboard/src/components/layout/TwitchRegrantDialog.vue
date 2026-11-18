<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Guides the person through the additive Twitch re-grant, one step at a time: what the bot gains, then
// Twitch with the code already filled in, then the result. Nothing starts before "Continue to Twitch".
import { computed, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { type MissingScopes, useRegrantApproval, useStartRegrant } from '@/api/twitchHealth';
import CopyLinkButton from '@/components/layout/CopyLinkButton.vue';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import RegrantStatus from '@/components/layout/RegrantStatus.vue';
import { Button } from '@/components/ui/button';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from '@/components/ui/dialog';
import { Spinner } from '@/components/ui/spinner';
import CheckIcon from '~icons/nnz/check';
import ChevronDown from '~icons/nnz/chevron-down';

const props = withDefaults(defineProps<{ scopes?: MissingScopes['scopes'] }>(), { scopes: () => [] });
const open = defineModel<boolean>('open', { required: true });
const emit = defineEmits<{ approved: [] }>();

const { t, te } = useI18n();
const regrant = useStartRegrant();
const code = computed(() => regrant.data.value ?? null);
const { state } = useRegrantApproval(code);

const featureKey = (feature: string) => `common.regrantDialog.features.${feature}`;
// Plain words for each feature the missing scopes unlock; a feature without words stays out of the list.
const gains = computed(() => {
  const features = new Set(props.scopes.flatMap((item) => item.features));

  return [...features]
    .filter((feature) => te(featureKey(feature)))
    .map((feature) => t(featureKey(feature)));
});

const step = computed(() => {
  if (state.value === 'approved')
    return 'done';

  if (state.value === 'waiting')
    return 'code';

  if (state.value !== 'idle' || regrant.isError.value)
    return 'retry';

  return 'intro';
});

// The page drops a prompt it holds itself (not read from the bot) when Twitch says yes.
watch(step, (now) => {
  if (now === 'done') {
    emit('approved');
  }
});

// A closed dialog starts fresh next time: the old code is gone and the poll stops.
watch(open, (now) => {
  if (!now) {
    regrant.reset();
  }
});
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent data-slot="regrant-dialog" :show-close-button="false" class="sm:max-w-md">
      <template v-if="step === 'done'">
        <div class="flex flex-col items-center gap-3 text-center">
          <CheckIcon class="size-10 text-success" aria-hidden="true" />
          <DialogTitle>{{ $t('common.regrantDialog.doneTitle') }}</DialogTitle>
          <DialogDescription>{{ $t('common.regrant.approved') }}</DialogDescription>
        </div>
        <DialogFooter>
          <Button data-action="regrant-done" @click="open = false">
            {{ $t('common.regrantDialog.done') }}
          </Button>
        </DialogFooter>
      </template>

      <template v-else>
        <div class="flex flex-col gap-1.5">
          <DialogTitle>{{ $t('common.regrantDialog.title') }}</DialogTitle>
          <DialogDescription>{{ $t('common.regrantDialog.intro') }}</DialogDescription>
        </div>

        <div v-if="step === 'intro'" class="flex flex-col gap-3 text-sm">
          <template v-if="gains.length > 0">
            <p class="m-0 font-medium">{{ $t('common.regrantDialog.unlocks') }}</p>
            <ul class="m-0 flex list-disc flex-col gap-1 pl-5">
              <li v-for="gain in gains" :key="gain">{{ gain }}</li>
            </ul>
          </template>
          <Collapsible v-if="scopes.length > 0">
            <CollapsibleTrigger as-child>
              <Button variant="link" size="sm" class="group h-auto! p-0 text-muted-foreground">
                {{ $t('common.regrantDialog.scopeNames') }}
                <ChevronDown
                  class="transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
                  aria-hidden="true"
                />
              </Button>
            </CollapsibleTrigger>
            <CollapsibleContent>
              <ul class="m-0 mt-2 flex max-h-40 list-none flex-wrap gap-x-3 gap-y-1 overflow-y-auto p-0">
                <li v-for="item in scopes" :key="item.scope">
                  <code class="font-mono text-xs text-muted-foreground">{{ item.scope }}</code>
                </li>
              </ul>
            </CollapsibleContent>
          </Collapsible>
        </div>

        <ol v-else-if="step === 'code' && code" class="m-0 flex list-none flex-col gap-4 p-0 text-sm">
          <li class="flex flex-col gap-2">
            <span>{{ $t('common.regrantDialog.stepOpen') }}</span>
            <Button as-child class="self-start">
              <a
                :href="code.verificationUri"
                target="_blank"
                rel="noopener noreferrer"
                data-action="regrant-open-twitch"
              >
                {{ $t('common.regrantDialog.openTwitch') }}
              </a>
            </Button>
          </li>
          <li class="flex flex-col gap-2">
            <span>{{ $t('common.regrantDialog.stepCode') }}</span>
            <div class="flex items-center gap-2">
              <span
                data-slot="regrant-code"
                class="rounded-md bg-muted px-3 py-1.5 font-mono text-lg font-semibold tracking-widest"
              >{{ code.userCode }}</span>
              <CopyLinkButton :url="code.userCode" :label="$t('common.regrantDialog.copyCode')" icon-only />
            </div>
          </li>
          <li class="flex items-center gap-2 text-muted-foreground">
            <Spinner aria-hidden="true" />
            <span>{{ $t('common.regrantDialog.stepApprove') }}</span>
          </li>
        </ol>

        <div v-else-if="step === 'retry'" class="flex flex-col gap-2">
          <RegrantStatus v-if="state !== 'idle'" :state="state" />
          <InlineNotice v-else tone="error" compact>{{ $t('common.regrantDialog.failed') }}</InlineNotice>
        </div>

        <DialogFooter>
          <Button variant="ghost" data-action="regrant-cancel" @click="open = false">
            {{ $t('common.regrantDialog.cancel') }}
          </Button>
          <Button
            v-if="step === 'intro'"
            data-action="regrant-start"
            :disabled="regrant.isPending.value"
            @click="regrant.mutate()"
          >
            <Spinner v-if="regrant.isPending.value" aria-hidden="true" />
            {{ $t('common.regrantDialog.start') }}
          </Button>
          <Button
            v-else-if="step === 'retry'"
            data-action="regrant-retry"
            :disabled="regrant.isPending.value"
            @click="regrant.mutate()"
          >
            {{ $t('common.regrantDialog.retry') }}
          </Button>
        </DialogFooter>
      </template>
    </DialogContent>
  </Dialog>
</template>
