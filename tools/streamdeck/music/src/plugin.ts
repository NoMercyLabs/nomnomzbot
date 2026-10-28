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

import streamDeck from "@elgato/streamdeck";
import { automationClient, runDeviceFlowLoop } from "@nomnomzbot/streamdeck-shared";
import { nowPlayingState } from "./nowPlaying/state.js";

import { PlayAction } from "./actions/play.js";
import { PauseAction } from "./actions/pause.js";
import { PlayPauseAction } from "./actions/playPause.js";
import { NowPlayingAction } from "./actions/nowPlaying.js";
import { NextAction } from "./actions/next.js";
import { PreviousAction } from "./actions/previous.js";
import { SetVolumeAction } from "./actions/setVolume.js";
import { VolumeUpAction } from "./actions/volumeUp.js";
import { VolumeDownAction } from "./actions/volumeDown.js";
import { VolumeMuteAction } from "./actions/volumeMute.js";
import { SeekAction } from "./actions/seek.js";
import { SetShuffleAction } from "./actions/setShuffle.js";
import { ToggleShuffleAction } from "./actions/toggleShuffle.js";
import { SetRepeatAction } from "./actions/setRepeat.js";
import { CycleRepeatAction } from "./actions/cycleRepeat.js";
import { TransferDeviceAction } from "./actions/transferDevice.js";
import { SaveTrackAction } from "./actions/saveTrack.js";
import { UnsaveTrackAction } from "./actions/unsaveTrack.js";
import { ToggleSavedAction } from "./actions/toggleSaved.js";
import { AddToPlaylistAction } from "./actions/addToPlaylist.js";
import { RemoveFromPlaylistAction } from "./actions/removeFromPlaylist.js";
import { FollowArtistAction } from "./actions/followArtist.js";
import { UnfollowArtistAction } from "./actions/unfollowArtist.js";

streamDeck.logger.setLevel("info");

// One shared connection + state for every key instance (streamdeck-plugin.md P2).
for (const registration of [
  new PlayAction(),
  new PauseAction(),
  new PlayPauseAction(),
  new NowPlayingAction(),
  new NextAction(),
  new PreviousAction(),
  new SetVolumeAction(),
  new VolumeUpAction(),
  new VolumeDownAction(),
  new VolumeMuteAction(),
  new SeekAction(),
  new SetShuffleAction(),
  new ToggleShuffleAction(),
  new SetRepeatAction(),
  new CycleRepeatAction(),
  new TransferDeviceAction(),
  new SaveTrackAction(),
  new UnsaveTrackAction(),
  new ToggleSavedAction(),
  new AddToPlaylistAction(),
  new RemoveFromPlaylistAction(),
  new FollowArtistAction(),
  new UnfollowArtistAction(),
]) {
  streamDeck.actions.registerAction(registration);
}

automationClient.onNowPlaying((payload) => nowPlayingState.apply(payload));
automationClient.onDisconnected(() => {
  streamDeck.logger.warn("Automation token lost — restarting the device pairing flow.");
  void runDeviceFlowLoop(() => void automationClient.connectStream());
});

// D9: the plugin drives pairing itself from launch — no dashboard interaction. Already-paired
// installs return immediately; a fresh install starts polling right away.
void runDeviceFlowLoop(() => void automationClient.connectStream());

void automationClient.ensureFreshToken().then(() => automationClient.connectStream());
setInterval(() => void automationClient.ensureFreshToken(), 24 * 60 * 60 * 1000);

await streamDeck.connect();
