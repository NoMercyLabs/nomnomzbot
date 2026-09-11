// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import streamDeck from "@elgato/streamdeck";
import { automationClient } from "./connection/automationClient.js";
import { runDeviceFlowLoop } from "./connection/pairing.js";
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

import { SwitchSceneAction } from "./actions/obsSwitchScene.js";
import { ToggleMuteAction } from "./actions/obsToggleMute.js";
import { StartStreamingAction } from "./actions/obsStartStreaming.js";
import { StopStreamingAction } from "./actions/obsStopStreaming.js";
import { ToggleStreamingAction } from "./actions/obsToggleStreaming.js";
import { StartRecordingAction } from "./actions/obsStartRecording.js";
import { StopRecordingAction } from "./actions/obsStopRecording.js";
import { ToggleRecordingAction } from "./actions/obsToggleRecording.js";
import { ToggleReplayBufferAction } from "./actions/obsToggleReplayBuffer.js";
import { ToggleVirtualCamAction } from "./actions/obsToggleVirtualCam.js";
import { SetPreviewSceneAction } from "./actions/obsSetPreviewScene.js";
import { SetSourceAction } from "./actions/obsSetSource.js";
import { FilterAction } from "./actions/obsFilter.js";
import { TransitionAction } from "./actions/obsTransition.js";
import { InputVolumeAction } from "./actions/obsInputVolume.js";
import { MediaAction } from "./actions/obsMedia.js";
import { HotkeyAction } from "./actions/obsHotkey.js";
import { RefreshBrowserAction } from "./actions/obsRefreshBrowser.js";
import { ScreenshotAction } from "./actions/obsScreenshot.js";
import { SaveReplayAction } from "./actions/obsSaveReplay.js";
import { RequestAction } from "./actions/obsRequest.js";
import { CallVendorAction } from "./actions/obsCallVendor.js";

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
  new SwitchSceneAction(),
  new ToggleMuteAction(),
  new StartStreamingAction(),
  new StopStreamingAction(),
  new ToggleStreamingAction(),
  new StartRecordingAction(),
  new StopRecordingAction(),
  new ToggleRecordingAction(),
  new ToggleReplayBufferAction(),
  new ToggleVirtualCamAction(),
  new SetPreviewSceneAction(),
  new SetSourceAction(),
  new FilterAction(),
  new TransitionAction(),
  new InputVolumeAction(),
  new MediaAction(),
  new HotkeyAction(),
  new RefreshBrowserAction(),
  new ScreenshotAction(),
  new SaveReplayAction(),
  new RequestAction(),
  new CallVendorAction(),
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
