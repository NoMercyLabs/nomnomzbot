// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.setup.ui

import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.feature.setup.state.FakeBotAuthApi
import bot.nomnomz.dashboard.feature.setup.state.FakeConnectLauncher
import bot.nomnomz.dashboard.feature.setup.state.FakeSetupChannelSettingsApi
import bot.nomnomz.dashboard.feature.setup.state.FakeSetupChannelsApi
import bot.nomnomz.dashboard.feature.setup.state.FakeSystemApi
import bot.nomnomz.dashboard.feature.setup.state.SetupController
import bot.nomnomz.dashboard.feature.setup.state.wizard
import kotlin.test.Test

// Regression coverage for the first-run wizard's credential fields ignoring keystrokes (owner-reported,
// reproduced live via browser automation: SetupController.onFieldChange fired with the correct value on
// EVERY keystroke — the controller's state layer was never at fault, and a pure SetupControllerTest proves
// it — yet the Client ID box stayed visibly empty forever. Root cause: the old CredentialField read
// `controller.valueOf(...)` fresh from a plain, non-observable MutableMap on every recomposition instead of
// holding real Compose state, so its own recomposition scope never re-ran. A state-machine-only test cannot
// catch this class of bug — it has to mount the composable and assert what's actually ON SCREEN, the same
// way [bot.nomnomz.dashboard.feature.settings.ui.TwitchAppCredentialsCard]'s proven
// `remember { mutableStateOf(...) }` pattern (now mirrored in [CredentialField]) makes this pass.
@OptIn(ExperimentalTestApi::class)
class SetupWizardScreenTest {

    @Test
    fun typing_into_the_twitch_client_id_field_renders_the_typed_characters() = runComposeUiTest {
        val api = FakeSystemApi(wizard = wizard(twitch = false, bot = false), ready = false)
        val controller =
            SetupController(
                systemApi = api,
                connectLauncher = FakeConnectLauncher(),
                botAuthApi = FakeBotAuthApi(),
                channelsApi = FakeSetupChannelsApi(bot.nomnomz.dashboard.core.network.ApiResult.Ok(bot.nomnomz.dashboard.core.network.ChannelSummary(id = "ch1"))),
                channelSettingsApi = FakeSetupChannelSettingsApi(),
                onReadyToSignIn = { true },
            )

        // The screen collects controller.state via collectAsStateWithLifecycle(), which needs a STARTED
        // LifecycleOwner — the real app gets one from its platform entry point (ComposeViewport / desktop
        // application {}); a bare runComposeUiTest host provides none, so the test supplies a minimal one.
        val testLifecycleOwner: LifecycleOwner =
            object : LifecycleOwner {
                // createUnsafe() skips the main-thread enforcement `LifecycleRegistry(this)` applies —
                // exactly the escape hatch the androidx.lifecycle API offers for tests like this one, which
                // run the Compose UI test harness off Android's main-thread concept entirely.
                override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
            }
        // LifecycleRegistry only accepts one state transition at a time (CREATED → STARTED → RESUMED),
        // never a direct jump — walk it through the same sequence a real host would fire.
        (testLifecycleOwner.lifecycle as LifecycleRegistry).apply {
            handleLifecycleEvent(Lifecycle.Event.ON_CREATE)
            handleLifecycleEvent(Lifecycle.Event.ON_START)
            handleLifecycleEvent(Lifecycle.Event.ON_RESUME)
        }

        setContent {
            CompositionLocalProvider(LocalLifecycleOwner provides testLifecycleOwner) {
                NomNomzTheme {
                    SetupWizardScreen(controller = controller)
                }
            }
        }

        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("Client ID").fetchSemanticsNodes().isNotEmpty()
        }

        // The label Text("Client ID") and the actual editable BasicTextField both carry that substring in
        // their semantics, so select by the SetTextSubstitution action instead — the Client ID field is the
        // FIRST editable node this step renders (Client Secret is the second).
        onAllNodes(matcher = hasSetTextAction())[0].performTextInput("abc123test")

        // The regression: the old CredentialField (reading `controller.valueOf(...)` as a plain function call,
        // never real Compose state) fails this assertion — the box stays empty because nothing forced its own
        // recomposition scope to re-run after the keystroke, even though the controller's state was correct.
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("abc123test").fetchSemanticsNodes().isNotEmpty()
        }
    }
}
