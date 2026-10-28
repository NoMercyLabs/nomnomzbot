// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

// The icon of a step card: a check by its block kind, an action by the category the bot files it under
// (pipeline.category.<name>).
import SplitBranch from '~icons/nnz/split-branch';
import TreeView from '~icons/nnz/arrow-tree-view-right';
import Repeat from '~icons/nnz/repeat';
import Dices from '~icons/nnz/dices';
import ShieldAlert from '~icons/nnz/shield-alert';
import Branching from '~icons/nnz/arrow-branching-right';
import Macro from '~icons/nnz/macro';
import ChatBubble from '~icons/nnz/chat-bubble';
import Moderator from '~icons/nnz/moderator';
import Note from '~icons/nnz/note-double';
import Volume from '~icons/nnz/volume-two';
import Mic from '~icons/nnz/mic';
import Route from '~icons/nnz/route';
import VideoSettings from '~icons/nnz/video-settings';
import Webcam from '~icons/nnz/webcam';
import Layout from '~icons/nnz/layout';
import MessageBubbles from '~icons/nnz/message-bubbles';
import Coins from '~icons/nnz/coins';
import NumberList from '~icons/nnz/number-list';
import Controller from '~icons/nnz/controller';
import GiftCard from '~icons/nnz/gift-card';
import User from '~icons/nnz/user';
import Film from '~icons/nnz/film';
import Shuffle from '~icons/nnz/shuffle';
import Quote from '~icons/nnz/quote';
import Star from '~icons/nnz/star';
import LiveStream from '~icons/nnz/live-stream';
import DataBase from '~icons/nnz/data-base';
import Link from '~icons/nnz/link';
import Code from '~icons/nnz/code';
import CreditCard from '~icons/nnz/credit-card-front';
import type { Component } from 'vue';

import { lookup } from '@/lib/array';

import type { BlockKind } from '@/api/pipelines';

const BLOCK_ICONS = {
  if: SplitBranch,
  switch: TreeView,
  switch_case: TreeView,
  loop: Repeat,
  random_branch: Dices,
  random_case: Dices,
  try: ShieldAlert,
  detached_step: Branching,
} as const satisfies Record<BlockKind, Component>;

const CATEGORY_ICONS = {
  billing: CreditCard,
  chat: ChatBubble,
  code: Code,
  counters: NumberList,
  discord: MessageBubbles,
  economy: Coins,
  flow: Route,
  games: Controller,
  giveaways: GiftCard,
  identity: User,
  media: Film,
  moderation: Moderator,
  music: Note,
  obs: VideoSettings,
  pick_lists: Shuffle,
  quotes: Quote,
  rewards: Star,
  sound: Volume,
  stream: LiveStream,
  tts: Mic,
  viewer_data: DataBase,
  vts: Webcam,
  webhooks: Link,
  widgets: Layout,
} as const satisfies Record<string, Component>;

export function blockIcon(kind: BlockKind): Component {
  return BLOCK_ICONS[kind];
}

// categoryKey is the LocalizedText key, for example "pipeline.category.chat".
export function actionIcon(categoryKey: string | undefined): Component {
  const name = categoryKey?.split('.').at(-1) ?? '';

  return lookup(CATEGORY_ICONS, name) ?? Macro;
}
