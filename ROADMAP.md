# Guida Scripting Extraction Roadmap

This roadmap is an internal engineering checklist for extracting Guida's scriptable core into this standalone Apache-2.0 repository.

The public project should be an embeddable scripting runtime and host API framework. Guida's browser host, WebView2 implementation, scraping/extraction logic, network capture/interception, screenshots, OCR/VLM services, and desktop UI integrations remain private for now.

## Goal

- Extract the reusable scripting runtime from Guida into a clean, standalone .NET project.
- Keep the public surface focused on script execution, task lifecycle, language engines, API metadata, and host capability abstractions.
- Let the private Guida desktop app consume this repository as a package or project reference.
- Preserve Guida's existing script behavior during the first integration pass.
- License this repository under Apache-2.0.

## Compatibility Target

- The end-state compatibility bar is that Guida can repoint its scripting runtime at this repository without changing the script-facing public API.
- Public SDK contracts should be host-neutral, but Guida-compatible by construction.
- Existing `g.*` names, argument shapes, return shapes, status strings, workflow semantics, queue semantics, module syntax, and generated TypeScript expectations should remain compatible unless a divergence is deliberately accepted.
- Guida-internal service wiring may change behind adapters, but existing scripts should not need a migration just because runtime code moved into this repository.
- Any intentional divergence from current script-facing behavior must be recorded in `COMPATIBILITY_TRACKER.md` before implementation.

## Non-Goals For The First Public Release

- No WebView2 host implementation.
- No browser tab manager.
- No DOM/page automation implementation.
- No web scraping or extraction implementation.
- No network capture or request interception implementation.
- No screenshots, OCR, VLM, Whisper, TTS, or model runtime implementation.
- No WPF, AvalonDock, or AvalonEdit editor surface.
- No Guida desktop app assets, private docs, bundled model files, or generated artifacts that expose closed APIs.
- No promise that the full current Guida `g.*` API ships in the first public release; when a surface is extracted or promoted, it should preserve Guida-compatible script behavior unless explicitly tracked as a divergence.

## Current Baseline

- Phase 0 repository setup has been implemented.
- `LICENSE` is already Apache-2.0.
- The repo has a neutral public `README.md`, `NOTICE`, `THIRD_PARTY_NOTICES.md`, CI placeholder, package metadata, and coverage-enabled xUnit test project.
- The solution targets plain `net9.0` and is pinned to .NET SDK `9.0.308` through `global.json`.
- Use `-m:1` for solution-level restore, build, and test commands because the current Windows/sandbox environment has unreliable parallel MSBuild/Roslyn named-pipe behavior.
- Phase 2 has implemented host-neutral capabilities for logging, document loading, workspace access, HTTP, secrets primitives, store, queue, worker jobs, search, and workflow ledger core.
- Workflow work currently covers ledger lifecycle, bulk mutation, schema validation/provider contracts, queue/worker bridge helpers, workspace layout/discovery, active-workflow overlay modeling, workspace module-resolution helpers, workflow workspace management/read models, and workflow ledger administration/read models; script-facing adapters remain later slices.
- Phase 3 has started with dependency-free public API registry descriptor contracts and an initial extracted-capability registry for store, queue, workers, workflow ledger/workflows, and workspace access. TypeScript, docs, manifest, and completion generators remain later Phase 3 slices.
- `ROADMAP.md` remains an internal engineering checklist and is not public-facing.

## Phase 0: Repository Baseline — Done

Create the public repository shape before extracting behavior. This phase should be small, buildable, and free of private Guida implementation code.

Deliverables:

- Keep `LICENSE` as Apache-2.0.
- Add `NOTICE` with Guida attribution and a trademark note clarifying that Guida names and marks are not licensed as trademarks.
- Add `THIRD_PARTY_NOTICES.md` with an initial placeholder section and a note that dependency notices must be updated before copying dependency-backed code.
- Expand `README.md` to describe this project as the embeddable scripting runtime and host API framework used by Guida.
- Make the README explicit that browser automation implementations are host-provided and are not included in this public package.
- Create `Guida.Scripting.sln`.
- Create `src/Guida.Scripting/Guida.Scripting.csproj` targeting plain `net9.0`.
- Create `tests/Guida.Scripting.Tests/Guida.Scripting.Tests.csproj` targeting `net9.0`.
- Use xUnit as the test framework with `Microsoft.NET.Test.Sdk`.
- Use FluentAssertions only if it is intentionally added as a test dependency during skeleton creation; otherwise keep assertion dependencies minimal.
- Add package metadata placeholders to the runtime project:
  - `PackageId` = `Guida.Scripting`;
  - license metadata = Apache-2.0;
  - repository URL placeholder;
  - XML documentation generation enabled;
  - package README metadata;
  - Source Link placeholder, but do not add a Source Link package until the repository URL is final.
- Add CI placeholders for build, test, formatting, and package validation.
- Keep all generated build output out of source control.

Implementation defaults:

- Use SDK-style projects.
- Enable nullable reference types and implicit usings.
- Keep the runtime assembly namespace under `Guida.Scripting`.
- Keep tests under `Guida.Scripting.Tests`.
- Do not add WPF, WebView2, AvalonDock, AvalonEdit, OCR, model runtime, or private Guida references.
- Do not copy generated Guida API manifests, private docs, sample scripts, assets, or closed implementation files in this phase.

Acceptance criteria:

- `dotnet restore Guida.Scripting.sln -m:1` succeeds from a clean checkout.
- `dotnet build Guida.Scripting.sln -c Debug --no-restore -m:1` succeeds from a clean checkout.
- `dotnet test Guida.Scripting.sln -c Debug --no-build -m:1` succeeds from a clean checkout.
- The repo builds without referencing WPF, WebView2, AvalonDock, AvalonEdit, Tesseract, Whisper, NAudio, OCR/VLM, native model runtime packages, or Guida desktop app assemblies.
- The README clearly states that browser automation implementations are not included in the public release.
- Package metadata is present but clearly marked as placeholder where repository details are not final.
- Notices exist before dependency-backed code is copied into the repository.

## Phase 1: Core Runtime Extraction

Extract the engine-neutral runtime before bringing over concrete JavaScript, Lua, or Janet engines. This phase defines the contracts and behavior that engine packages will later implement.

First implementation slice:

- Add clean public runtime contracts rather than directly copying private engine wiring.
- Add language detection, execution request/result types, debugger contracts, and a registration-based engine factory.
- Keep the factory free of concrete engine constructors and private `ScriptContext` assumptions.
- Do not port task lifecycle management, preprocessing, ClearScript, LuaCSharp, or JanetSharp in this slice.
- Use private Guida files as reference material only where they are engine-neutral and license-safe.

Runtime model:

- Define script language detection for JavaScript, TypeScript, Lua, Janet, and unknown input.
- Define script execution result types that represent success, failure, returned value, error message, exception details when available, elapsed time, and cancellation or timeout state.
- Define task lifecycle types:
  - task status;
  - task origin;
  - task record;
  - task start options;
  - task completion metadata;
  - task lookup and cleanup behavior.
- Define a task lifecycle manager that can start, track, complete, fail, cancel, time out, register externally-created tasks, and remove completed task records according to explicit cleanup rules.
- Define debugger contracts only as engine-neutral interfaces and event/data models. Do not port Jint, V8, Lua, or editor-specific debugger implementations in this phase.
- Define timeout and cancellation behavior at the runtime boundary so all future engines share one policy.

Engine contracts:

- Extract `IScriptEngine` as the common execution contract.
- Define engine creation options separately from per-run execution options.
- Extract `ScriptEngineFactory` without private Guida host assumptions.
- Make the factory select engines by language and fail with clear errors for unsupported languages.
- Keep concrete engine registration extensible so Phase 4 can add ClearScript, LuaCSharp, and JanetSharp implementations without changing the task manager API.

Phase 1 closeout:

- Do not extract legacy `#include` support into the public core.
- Do not add public source concatenation preprocessing, include-chain mapping, include path traversal handling, or include expansion tests.
- Do not add a generic public module resolver in the core runtime.
- Keep JavaScript imports as standard ES module syntax in future ClearScript adapters so physical `DocumentInfo` and URI-backed debugger behavior remain intact.
- Keep Lua and Janet module loading tied to their native module systems in future concrete engine adapters.
- Defer `@timeout` and `@require-context` parsing from the public core. Prefer explicit execution options and Phase 2 host capability availability/error modeling over comment-based runtime policy.

Public dependency rules:

- Keep namespace names under `Guida.Scripting` for the initial extraction unless a later rename is deliberate.
- Keep public runtime code free of `System.Windows`, `Microsoft.Web.WebView2`, `AvalonDock`, `ICSharpCode.AvalonEdit`, WPF types, browser implementation types, and private Guida services.
- Do not expose browser automation, scraping, network capture, screenshot, OCR/VLM, model runtime, desktop pane, or editor concepts as concrete public runtime APIs.
- Where a private Guida type currently appears in runtime behavior, introduce a public abstraction or defer that behavior to Phase 2.

Acceptance criteria:

- Unit tests cover task start, completion, failure, cancellation, timeout, task lookup, task cleanup, and external task registration.
- Language detection tests cover `.js`, `.mjs`, `.cjs`, `.ts`, `.lua`, `.janet`, and unknown extensions.
- Factory tests cover supported language selection, unsupported language errors, and registration of future engine implementations without private host dependencies.
- Runtime tests verify clear timeout and cancellation results without depending on any concrete script engine package.
- Public runtime project compiles on plain `net9.0`.

## Phase 2: Host Capability Abstractions

- Replace Guida's current `ScriptContext` service bag with explicit host capability contracts.
- Define public abstractions for:
  - logging;
  - module/document loading;
  - filesystem-safe workspace access;
  - HTTP;
  - secrets;
  - store;
  - queue;
  - search;
  - workers;
  - LLM providers;
  - optional clipboard/UI hooks.
- Split workflow-related work into public-safe slices instead of a single generic workflow abstraction:
  - workflow ledger capability;
  - workflow ledger schema and transition support;
  - workflow queue/worker bridge helpers;
  - workflow workspace layout and discovery;
  - workflow workspace management, inspection, switching, and portable import/export;
  - workflow ledger administration and read models.
- Model the workflow ledger around Guida's observable ledger behavior: runs, items, events, artifacts, claims, retries, releases, completion, failure, cancellation, and dead-lettering.
- Account for current ledger admin and read-model operations: bulk retry/cancel/dead-letter, retention preview/prune, export/import, overview, transition graph, and flow evidence. Bulk item operations are on the core ledger contract; retention, history import/export, and read models are on the separate administration capability.
- Keep workflow ledger persistence host-owned. Do not add SQLite or other durable storage dependencies to the SDK core.
- Expose transition validation and schema provider concepts, but keep `workflow-ledger.schema.json` file loading tied to workspace/layout support rather than the bare ledger core.
- Preserve workflow queue/worker bridge behavior through public host-neutral helpers or adapter contracts:
  - workflow enqueue envelopes;
  - enqueue event idempotency keys;
  - task-id-aware worker workflow context.
- Model unavailable capabilities explicitly so scripts receive clear errors instead of null-reference failures.
- Keep browser-shaped concepts as abstractions only, with no WebView2 or scraping implementation in this repo.
- Provide minimal no-op, in-memory, or test implementations only where they are useful for examples and tests.

Acceptance criteria:

- Public engine contexts depend only on public capability interfaces.
- Tests verify clear errors when a script calls a capability that the host did not provide.
- Workflow capability tests preserve existing script-facing `g.workflow`, `g.workflows`, and `g.worker.workflow` behavior when those surfaces are extracted.
- Workflow ledger core tests cover lifecycle, cancellation, bulk mutation, and schema validation/provider behavior.
- Workflow queue/worker bridge tests cover enqueue envelope payloads, enqueue idempotency, and task-id-aware worker workflow context.
- Workflow workspace layout tests cover global and workflow-scoped scripts, libraries, views, config discovery, workflow manifests, workflow ledger schema loading, active-workflow overlays, and module/config path resolution.
- Workflow workspace management tests cover listing, active-workflow switching, inspection, summaries, creation, manifest enablement, portable import/export, and cancellation.
- Workflow ledger administration tests cover retention preview/prune, history export/import, overview summaries, transition graph schema overlays, flow evidence, and cancellation.
- `COMPATIBILITY_TRACKER.md` is updated when each workflow slice lands.
- No public project file references closed-source or desktop-only packages.

## Phase 3: Public API Registry

- Add dependency-free descriptor models for script API types, parameters, properties, functions, interfaces, groups, and registry documents.
- Add a registry provider contract that hosts and future language adapters can consume without depending on generators or private host services.
- Add an initial public-safe extracted-capability registry for:
  - `g.store`;
  - `g.queue`;
  - `g.workers`;
  - `g.worker`;
  - `g.worker.workflow`;
  - `g.workflow`;
  - `g.workflows`;
  - logical workspace access.
- Split API metadata into public-safe and private Guida-only groups.
- Generate TypeScript definitions only from the public-safe registry.
- Generate API docs/manifests only from the public-safe registry.
- Treat `g.workflow` ledger APIs, `g.workflows` workflow discovery/switching APIs, and `g.worker.workflow` worker-item helpers as distinct compatibility targets.
- Keep admin, MCP-only, UI-only, and ledger maintenance operations out of script-facing API metadata unless they are deliberately promoted to public script APIs.
- Keep private API groups for browser DOM automation, tabs, interception, capture, screenshots, scraping, extraction, desktop panes, and closed Guida workflow behavior out of this repo.
- Add leak tests that fail if private namespace names or browser implementation types appear in generated public artifacts.
- Do not copy generated private manifests or generated definition files into this repository. Future generators should emit public artifacts from the SDK registry at build/test time.

Acceptance criteria:

- API registry descriptor tests cover type formatting, structural validity, duplicate detection, namespace/group metadata, stable script-facing names, key TypeScript declaration strings, and public/private namespace boundaries.
- Public generated artifacts do not include private namespaces such as browser DOM automation, tabs, interception, capture, screenshots, scraping, extraction, or desktop panes.
- API registry tests prove every public method has stable names, docs, parameters, and return metadata.
- Extracted API metadata preserves Guida-compatible script-facing names and TypeScript shapes unless the compatibility tracker records an intentional divergence.

## Phase 4: Engine Implementations

- Port ClearScript integration for JavaScript.
- Port LuaCSharp integration for Lua.
- Port JanetSharp integration for Janet.
- Keep engine-specific interop helpers isolated behind common runtime contracts.
- Preserve shared behavior for logging, return values, task cancellation, host capability errors, and timeout handling.
- Document language-specific differences where exact parity is not practical.

Acceptance criteria:

- JavaScript, Lua, and Janet engines can run basic scripts through the common task manager.
- Parity tests cover common public APIs across all supported languages.
- Engine tests cover exceptions, return values, async behavior where applicable, cancellation, and host capability calls.

## Phase 5: Private Guida Host Integration

- Update the private Guida desktop app to consume this repository as a project reference during extraction.
- Move private implementations into the Guida app or a private host adapter package:
  - WebView2 execution;
  - browser tab management;
  - DOM/page automation;
  - web scraping and extraction;
  - network capture and request interception;
  - screenshots and OCR/VLM;
  - WPF panes, command palette, editor, and desktop UI behavior.
- Implement adapters from Guida private services to public host capability interfaces.
- Keep adapter tests in the private Guida repository.
- Avoid changing user-facing script behavior in the first integration pass.

Acceptance criteria:

- Existing Guida scripts continue to run through the extracted runtime.
- Guida can repoint scripting runtime integration at this repository without public script API changes for extracted surfaces.
- Existing Guida scripting tests either move to this repo if public-safe or remain private if they cover closed implementations.
- Private Guida builds continue to support current Debug, Debug_Full, Release, and Release_Full modes.

## Phase 6: Packaging And Release

- Publish preview NuGet packages only after the private Guida app successfully consumes the extracted runtime.
- Start with `0.1.0-alpha.1`.
- Mark APIs as unstable until `1.0`.
- Include XML docs, package README, Source Link, license metadata, repository metadata, and third-party notices.
- Add a compatibility note explaining that browser automation implementations are provided by hosts and are not part of the public package.
- Add release notes documenting public APIs, known limitations, and migration notes for Guida.

Acceptance criteria:

- NuGet package validates license, README, repository URL, symbols, and Source Link.
- Consumers can run a minimal script host example without private Guida dependencies.
- Private Guida can consume the packaged artifact or the same source tree without conditional hacks.

## License And Dependency Guardrails

- Keep Apache-2.0 as the project license.
- Keep third-party dependency notices current.
- Prefer MIT, BSD, and Apache-2.0 dependencies for the public core.
- Avoid MS-PL, Microsoft-proprietary, desktop-only, native model runtime, or ambiguous-license dependencies in the core package.
- Do not redistribute model files or native runtime payloads unless their licenses are explicit and compatible.
- Do not copy private docs, generated manifests, sample scripts, or assets until their provenance is checked.
- If external contributors have touched extracted Guida code, confirm the right to publish that code under Apache-2.0 before copying it here.

## Done Criteria For Initial Public Release

- The repository builds and tests from a clean checkout.
- The public package has no WPF, WebView2, AvalonDock, AvalonEdit, Tesseract, Whisper, NAudio, OCR/VLM, or Guida desktop dependencies.
- JavaScript, Lua, and Janet can execute through one runtime abstraction.
- Public API metadata and generated TypeScript definitions include only public-safe capabilities.
- A minimal sample host demonstrates script execution, logging, timeout/cancellation, and at least one injected capability.
- The private Guida app consumes the extracted runtime without changing current user-facing script behavior.
