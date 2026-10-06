# AI Compute 0.3.0 — frontier progression

For Software Inc. Beta 1.8.42. Enable both the data and code mods. This version revises progression and compute balance, adds a player-first research gate and benchmark discovery, and fixes assistant selection and the hosted-model dependency definition.

## Starting and competition

AI and Chat Interface development unlock in **2018**. Coding Harness opens in 2019; Image Studio in 2022. These are fictional gameplay milestones, not historical model release dates.

With the code mod active during ordinary single-player simulation, unreleased rival AI projects are held until **six game months after your first commercial AI release**. Rivals then begin paid server training and must finish it before their evaluation/release delay. Keeping a model private does not open the race. The first public release date persists across saves, including after the original product is archived. Training gates cover queued and active rival projects.

This is a player-first campaign rule, not a universal guarantee: the game creates historical products during pre-simulation, where this controller is inactive. Existing rival products are preserved. Start in 2018 or earlier with both mod halves enabled for the intended campaign; late starts, prior saves, enabling the controller late, and other mods may already have competing models. A fresh game is required to validate the new data timeline. Do not expect an established 2000-era campaign to be retimed by this update.

| Model size | Unlock | Training months | Base capability ceiling | Reference capacity |
| --- | ---: | ---: | ---: | ---: |
| 1B specialist | 2018 | 1 | 65 | 10,000 |
| 7B | 2019 | 1.5 | 78 | 50,000 |
| 60B | 2020 | 2 | 88 | 200,000 |
| 175B | 2021 | 4 | 94 | 800,000 |
| 600B | 2022 | 6 | 98 | 3,000,000 |
| 1T | 2024 | 9 | 100 | 6,000,000 |

Reasoning and knowledge begin with the 2018 type unlock. Coding and structured knowledge arrive in 2019; advanced reasoning, code verification, enterprise integration, vision and speech arrive in 2021. Image generation arrives in 2022 and audio generation in 2023. Long context requires its corresponding feature, even if a context-size option has unlocked earlier. Sparse experts unlock in 2021, 8-bit serving in 2019, 4-bit in 2022, 32K context in 2020 and 128K in 2023. Code datasets unlock in 2019 and multimodal data in 2021.

## Compute has a wider useful range

Training retains a fixed active window; additional servers improve capability rather than shortening the deadline. The learning curve now reaches **50% of potential at 1x effective reference compute, 80% at 4x, and about 91% at 10x**. The old curve reached about 92% at 1x and almost saturated at 2x. Early ceilings are also higher. Reference capacity is 100 times the old values: the existing playtest already showed a 70,000-capacity server group, making the old 100-unit specialist reference trivial even at 1% allocation. This revision makes allocation and infrastructure matter at the native game's capacity scale. These percentages apply to each domain's effective compute ratio, after domain shares, complexity and architecture are accounted for; they are not the final overall benchmark.

Selected domains divide the delivered compute budget. Focus weights one selected domain three times as heavily. Preparation, datasets, selected features, precision and context still affect results. Scores remain bounded at 100. More capacity is useful over a larger range, but finite models retain diminishing returns. Weakly funded runs can score lower than under 0.2.5; this is deliberate separation between token investment and serious training.

Rivals use the same scoring rules and pay for virtual compute because they have no physical offices. Their hourly capacity budget is 0.35–1.0x reference, scaled by architecture and preparation. A well-funded player can materially outspend them. Already released models retain their recorded scores and immutable native quality. Unfinished training uses the new curve immediately.

The panel now shows projected final benchmark at the selected allocation, reference-load multiple, delivered capacity, invested capacity-months and power costs. The projection assumes full delivery for the remaining window; contention and outages can lower the result. Explicit pauses stop the training clock. Employees still handle preparation and beta evaluation.

## Discovery without an experienced AI publisher

Every public, unarchived AI model receives a benchmark-driven organic awareness floor. It depends on absolute benchmark and standing against the strongest current model, with the same rule for player and rivals. At the frontier, a score of 80 earns a 48% awareness target; a score of 40 against that frontier earns 3%. The maximum is 75%, leaving room for native marketing.

This uses native awareness, normalized to the category's awareness scale, and native compute-derived product quality. It does not invent publisher experience, directly award customers, transfer market share, or credit sales revenue. Price, features, distribution, hosting, reputation and the native sales simulation still matter. Long-term adoption and profitability require a live campaign test.

## Assistant selection and UI fixes

Choose a released model and press **Use selected model (lock provider)**. Both this button and automatic selection are now visible in Simple view. Choosing a dropdown entry alone previews that model; the button applies the persistent provider lock.

A locked model stays selected through an inference outage. If it is unavailable, archived or no longer licensed, assistance stops instead of silently using a competitor. Restore capacity/access or explicitly choose automatic selection. Preferences already saved by earlier versions are treated as locks. Owned models need a working physical inference group, and bonuses require employees doing eligible work; idle teams create no corporate demand. The panel reports the active assistant separately from the model being inspected.

Reopening restores the pinned model in the dropdown. Refresh preserves pending configuration/allocation/server selections. Owned successor revisions inherit the provider preference and inference assignment, including private successors. Paid external access transfers only to public successors. Text blocks fit their wrapped content to reduce empty space and avoid fixed-height clipping. Existing recovery boundaries remain in place.

## Hosted applications

The hosted model connection now requires **System** technology, matching the AI's mandatory reasoning module. It previously required Network, which early AI models could not supply before coding unlocked. The conversation-interface workload moves to Network to preserve distinct root specializations.

Apps still use the native dependency picker. Select a released, accessible model supporting the application's required technology level; a private rival model or an incompatible technology level can still be unavailable. Lower the app's technology level if needed. This corrects the data mismatch, but the revised picker interaction still needs live verification. The native dependency picker does not validate custom coding/image-generation benchmarks, so choose a suitable model for those application categories. Image Studio also requires a 2D Editor.

## Other retained systems

- Paid datasets, dense/sparse architectures, quantization, context choices and domain focus.
- Physical player servers, native electricity/maintenance and contention; no duplicate training charge.
- Corporate model licensing, capability-specific employee assistance and paid rival inference.
- Fine-tuning with a 90% warm checkpoint and half training window; full retraining and commercial subscription successors.
- Native recurring consumer subscriptions and optional estimated usage pricing, without duplicate revenue credits.
- Stable tier IDs, saved model/configuration/training records, and legacy labels for products lacking recorded training history.

## Build and validation

Run `tools/Build-AICompute.ps1`. The output is `build/AI-Compute-0.3.0-source.zip`. `tools/Install-AICompute.ps1` backs up matching installed mod folders before copying both halves. Restart the game after installing and enable the separate code-mod checkbox in Options > Mods.

Automated checks cover original and native-preprocessed C# 3 compilation, native security verification, recovery behavior, scoring/compute/training/serving rules, dated unlocks, the six-month frontier gate, locked-provider outages and missing access, discovery, hosted dependency specialization, native TyD parsing, XML ownership and native integration metadata. These do not execute the Unity simulation or render the new panel. Earlier 0.2.5 screenshots demonstrate the previous UI only. The 0.3.0 live acceptance checklist is in `tests/AICompute-playtest.md`.
