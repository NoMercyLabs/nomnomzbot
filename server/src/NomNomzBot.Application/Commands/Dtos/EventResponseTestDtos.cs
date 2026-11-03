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

namespace NomNomzBot.Application.Commands.Dtos;

/// <summary>One pipeline step a test fire ran. <paramref name="Output"/> is what the step produced, such as the line a send_message step sent.</summary>
public sealed record EventResponseTestStep(
    int Index,
    string ActionType,
    bool Succeeded,
    string? ErrorMessage,
    string? Output = null
);

/// <summary>
/// One request to read a line aloud during a test fire. <paramref name="Text"/> is the text that was asked for, or
/// null when a pipeline step asked and its text is not known. <paramref name="RefusedReason"/> is why the request
/// was refused, or null when it was accepted.
/// </summary>
public sealed record EventResponseTestSpeechLine(string? Text, string? RefusedReason);

/// <summary>
/// What a test fire of an event response put in front of viewers. Every field is something that really went
/// out; zero and empty are truthful answers. <see cref="Warnings"/> holds the codes of
/// <c>EventResponseTestWarnings</c>.
/// </summary>
/// <param name="EventType">The event type that was fired.</param>
/// <param name="Variables">The sample variables the response was given.</param>
/// <param name="ChatLine">The resolved line the response sent, or null when it sent none.</param>
/// <param name="TtsText">The text queued for speech, or null when none was queued from the response's own line.</param>
/// <param name="Alerts">The resolved overlay alerts sent, in order.</param>
/// <param name="PipelineSteps">The pipeline steps that ran, in order.</param>
/// <param name="SpeechLines">Every request to read a line aloud, from the response itself and from its pipeline steps, with the reason when one was refused.</param>
public sealed record EventResponseTestResult(
    string EventType,
    IReadOnlyDictionary<string, string> Variables,
    string? ChatLine,
    string? TtsText,
    IReadOnlyList<string> Alerts,
    IReadOnlyList<EventResponseTestStep> PipelineSteps,
    int ChatMessagesSent,
    int TtsQueued,
    int OverlaysShown,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<EventResponseTestSpeechLine> SpeechLines
);
