# Artificial Intelligence software and server-driven training

Investigated October 3, 2026, against installed **Software Inc. Beta 1.8.42**, Steam build `24837201`.

## Finding

Adding an Artificial Intelligence software type is straightforward. Making training advance through server resources instead of employee labor is technically feasible through a **hybrid data and C# mod**. The game exposes a general server-workload interface and editable software development progress. This does not appear to require changing the game's executable or replacing its development engine for an initial implementation.

This is a feasibility finding supported by static inspection and successful compilation. It is not an in-game proof: the probe has no ModMeta entry point, was not installed, and has never run in the game. The built-in source compiler/security checks, UI behavior, and persistence still need runtime validation.

## What already exists

The documented data-mod format supports new software types, categories, features, unlock years, and competitor company types. Its `Server` feature parameter describes demand per active user. It is useful for hosting an AI service after release, rather than representing a training job before release.

The documented SIPL entry points cover release, sales, daily simulation after release, copies and work-item changes. There is no documented recurring pre-release training callback. SIPL alone is therefore not a practical supported route for the requested development loop.

Source: [Data Modding](https://softwareinc.coredumping.com/wiki/index.php/Data_Modding).

Local inspection confirmed that the normal development server uses `SoftwareAlpha.HandleLoad(float)` to set a private `SourceControlBoost`. `DoWork(Actor, ...)` uses this as an employee effectiveness multiplier of `1 + boost * 0.1`; it also reduces bug generation. The server does not independently produce development work through this path.

## Verified integration points

These were checked in the installed assembly, not inferred from wiki wording:

| API | Why it matters |
| --- | --- |
| `IServerItem.GetLoadRequirement()` | A custom training job can request capacity. |
| `IServerItem.HandleLoad(float)` | The native allocator reports its served fraction; this is not an absolute GPU throughput measurement. |
| `IServerItem.UsesISP` | On-premises training can avoid being classified as internet traffic. This does not remove cloud-provider charges. |
| `GameSettings.RegisterWithServer(string, IServerItem, bool)` | Attach a training job to an existing server group. |
| `GameSettings.DeregisterServerItem(IServerItem)` | Remove demand on completion, cancellation or deactivation. |
| `GameSettings.GetAllServerGroups(...)`, `TryGetServerGroup(...)` | Inspect and choose infrastructure. |
| `ServerGroup.PowerSum`, `Available`, `Broken`, `IsCloud` | Read aggregate capacity, spare fraction and server status. |
| `TimeOfDay.OnHourPassed` | Advance training with simulation time, after native server processing. |
| `SoftwareWorkItem.Features` | Access the native per-feature development state. |
| `FeatureProgress.AddProgress(...)`, `AddQuality(...)` | Change progress and quality without passing an Actor. |
| `SoftwareWorkItem.RefreshWorkDevTime(...)`, `AllDone(...)` | Update derived development state. |
| `ModBehaviour.Serialize/Deserialize` | Persist progress and allocation settings per save. |
| `GUIWorkItem.AddBonusButton(...)`, `WindowManager.GenerateUI(...)` | Add access to a training panel without rebuilding the HUD. |

The server allocator requests load from every registered item, accounts for contention/fallback, calls `HandleLoad`, and sets server usage. Its on-premises capacity is a bandwidth abstraction, with date scaling applied through `Utilities.BandwidthFactor`. Existing hardware has no dedicated GPU/FLOP quantity in the inspected server interface. We must define an explicit conversion from native capacity to training compute units, or add a separate GPU capacity system.

Native utilization feeds furniture usage. Cloud usage also participates in native cost calculations. Electricity, cloud billing, fallback, outages and sharing behavior must be checked in-game before promising exact economics. Implementing `ILossable` is optional for allocation, but matters if training costs should flow into the project's native loss breakdown.

## Recommended initial implementation

Use a data definition for Artificial Intelligence, with optional categories such as language models, computer vision and recommendation systems. Keep the existing product, market, marketing and sales systems.

Use one C# controller for the training mechanic:

1. Detect eligible development projects and distinguish player-controlled work from simulated competitors.
2. Let employees perform model/data preparation through a short conventional design/setup portion.
3. Let the player choose a server group and a capacity budget in a training panel.
4. Register a separate `IServerItem` for that training job. Store the latest served fraction in the callback; advance once per simulation tick rather than assuming each callback represents a new hour.
5. Accumulate training work from allocated capacity and elapsed game time. Normalize for the game's days-per-month setting and native date scaling.
6. Prevent ordinary employee contribution from substituting for the controlled training work. An initial adapter could pause staff work on the relevant alpha task while progressing its training features, then restore normal work for evaluation/beta. Its scheduling and UI effects need a prototype.
7. Apply training progress, quality and completion flags consistently; resume the native evaluation/release path.
8. Save simple progress records keyed to native work-item/product IDs, then recreate transient workload objects and registrations after loading. Clean them up on cancel, release, quit and disable.

A possible gameplay flow is:

```text
Employees: prepare architecture and data
                 |
Servers: train model, using allocated capacity
                 |
Employees: evaluate, fix integration issues, release
                 |
Servers: serve released AI product, if applicable
```

Illustrative balancing equation, not an existing game rule:

```text
training work gained = allocated compute rate × served fraction × simulation time
```

Model size, data quality, architecture, and target capability can determine total work and eventual quality. More servers help until the job's requested capacity budget is met; they should not automatically provide unlimited quality. This is a simulation of model development, not actual machine-learning training on the user's PC.

## Integration depth

| Scope | Required integration | Assessment |
| --- | --- | --- |
| AI product with ordinary development and hosting | Data files, balance, localization, competitor definitions | Low; does not deliver compute-driven training. |
| Actual server-driven training with a custom panel and native development adapter | Data files plus C# workload/controller, progress/quality adapter, persistence and UI | Medium; public API route is compile-verified, runtime behavior remains untested. Recommended first target. |
| Separate native Training stage, mandatory completion before release, native project-management automation | Stage transition and promotion integration, all manual/automatic release routes, additional UI and scheduling work | High; may require runtime method patches or replacing native work items. Removing a release button alone is insufficient. |
| Independent GPU/VRAM hardware, distributed training, checkpoints, retraining, competing-company compute budgets and multiplayer parity | Custom infrastructure and simulation rules plus broader integration | High to very high; a substantial simulation extension. |

The moderate version can preserve native early-release behavior: undertraining produces a worse product. A strict requirement that an untrained model cannot be released is a separate feature and needs stronger promotion control.

Competitor definitions can make simulated companies sell AI products. That alone does not make them follow the player's physical training-server economy. `SimulatedCompany` has its own project-prototype/release simulation; equivalent compute spending and training delays need separate rules. Multiplayer additionally needs host authority and synchronized training state.

Optional training/GPU rack furniture could follow later. Reusing normal server groups first reduces the number of systems needed to demonstrate the mechanic.

## Validation completed

- Inspected server registration, hourly allocation, development progress, quality, promotion, and serialization APIs and IL.
- Compiled [TrainingApiProbe.cs](TrainingApiProbe.cs) against the installed game with the .NET Framework C# compiler using `/langversion:3`.
- Compiler exit code: **0**. Verified the interface implementation, registration calls, hourly events, persistence overrides and actor-free progress calls.
- Did not execute the probe, modify the installation, or open a save.

The probe's progress helper checks signatures only. Its fixed quality value and simplistic loop are not a production algorithm.

Game assembly SHA-256:

```text
C877DBC50F0B9B94BA3C07AA9125675792DE4DF74448E33BE799144757E58BD0
```

Inspection tooling is in `../../tools/Inspect-GameApi.ps1`. Adjacent `*-il.txt` and `api-members.txt` files preserve the local evidence.

## Next proof needed before a full build

The smallest useful playable prototype is one AI type, one training job, a server picker, a progress display and persistence. Verify:

- Zero staff can advance training with functioning servers; zero capacity cannot.
- Faster infrastructure increases progress within the allocation budget; competing workloads reduce it.
- Pause and game speed behave correctly, including different days-per-month settings.
- Training produces sensible native review/release quality, rather than merely a full progress bar.
- Save/reload preserves progress, with exactly one workload registration.
- Server removal, rename, outages, fallback, cancellation, mod disable and automatic project management do not leave stale demand or unintended employee progress.
- The in-game source compiler/security checker accepts the implementation before claiming Workshop compatibility.

The official code guide supports mod UI, save hooks, and Workshop distribution through source compilation: [Code Modding](https://softwareinc.coredumping.com/wiki/index.php/Code_Modding).

The developer has announced an upcoming server overhaul, including player-developed server products. That is a concrete future compatibility concern for this adapter; the inspected installed build still uses the APIs above. Source: [official Steam announcements](https://store.steampowered.com/oldnews/?appgroupname=Software+Inc.&appids=362620&feed=steam_community_announcements).
