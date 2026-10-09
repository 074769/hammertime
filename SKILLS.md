# HammerTime --- AI Coding Agent Skills

## Project identity

-   Repository: https://github.com/074769/hammertime
-   Product: HammerTime, an open-source GoldSrc level editor and a fork
    of Sledge.
-   Main solution: `HammerTime.sln`
-   Language/runtime: C# / .NET.
-   Rendering: Veldrid; the repository README currently describes the
    renderer as using DirectX 10.
-   License: BSD 3-Clause. Preserve existing copyright and license
    notices.

This file is guidance for coding agents working in this repository.
Treat the checked-out source code and project files as authoritative
when a detail here differs from the current branch.

## First steps for every task

1.  Inspect `README.md`, the relevant project files, and nearby code
    before making assumptions.
2.  Check `git status --short` before editing. Do not overwrite or
    discard pre-existing user changes.
3.  Trace the complete call path for the feature being changed,
    including UI events, data models, serialization, rendering, and
    tests where applicable.
4.  Prefer the smallest coherent change that solves the requested
    problem.
5.  Follow the local coding style and existing abstractions. Avoid broad
    refactors unless explicitly requested.
6.  Search for similar implementations elsewhere in the repository
    before adding a new pattern.
7.  After editing, review the full diff for unrelated changes, generated
    files, accidental whitespace churn, and compatibility regressions.

## Repository orientation

The solution contains multiple projects. Important project areas visible
in the repository include:

-   `HammerTime.GoldSource`: GoldSrc-specific editor and format
    behavior.
-   `HammerTime.Source`: Source-engine-related functionality; do not
    assume Source behavior is valid for GoldSrc.
-   `HammerTime.Formats.Copy`: format/copy-related functionality.
-   `Sledge.BspEditor`: BSP editor integration and document-level
    behavior.
-   `Sledge.BspEditor.Editing`: editing operations and document
    mutations.
-   `Sledge.BspEditor.Rendering`: viewport rendering.
-   `Sledge.BspEditor.Tools`: editor tools and interaction behavior.
-   `Sledge.Common`, `Sledge.DataStructures`, `Sledge.FileSystem`,
    `Sledge.Providers`, `Sledge.Rendering`, `Sledge.Shell`, and related
    `Sledge.*` projects: shared infrastructure.

These names are orientation, not a substitute for searching the current
checkout. Before choosing a project, inspect its `.csproj` and
references to confirm ownership of the code.

## Build and verification

-   Open `HammerTime.sln` in a compatible version of Visual Studio, or
    build it from a Developer PowerShell/terminal.
-   Before prescribing a .NET SDK version, inspect the solution, all
    relevant `.csproj` files, `global.json` if present, and repository
    build scripts. Do not blindly assume the newest SDK is compatible
    with this older codebase.
-   Restore dependencies before building if required.
-   Prefer building the smallest affected project first, then the full
    solution if practical.
-   Use the repository's existing build scripts and CI configuration as
    the source of truth.
-   Report the exact commands run and whether they succeeded. Never
    claim a build or test passed unless it was actually run.
-   If the environment cannot build the solution, state the blocker and
    perform available static checks instead.

## C# and architecture practices

-   Match the language version and framework APIs already used by the
    target project.
-   Avoid introducing new NuGet packages unless necessary; explain the
    trade-off if one is needed.
-   Avoid changing public APIs or serialized formats without checking
    all call sites and compatibility implications.
-   Keep UI code, editor/document state, file-format logic, and
    rendering responsibilities in their existing layers.
-   Avoid synchronous heavy work on the UI thread, especially file
    loading, parsing, texture work, and large geometry operations.
    Preserve thread-safety and UI-thread requirements of existing
    frameworks.
-   Dispose of streams, graphics resources, and other disposable objects
    using the patterns already used in the project.
-   Avoid premature optimization. For performance work, identify the hot
    path and measure or profile before redesigning it.

## GoldSrc-specific rules

GoldSrc behavior is not interchangeable with Source-engine behavior.
Confirm the relevant GoldSrc format and editor conventions before
implementing a feature.

### Texture mapping and UVs

-   Inspect the existing GoldSrc face/texture-axis representation and
    serialization before changing texture mapping.
-   Keep texture axes, offsets, scales, rotation, and UV interpretation
    consistent with the format's actual semantics.
-   Do not assume arbitrary UV vectors are valid simply because a
    similar editor supports them. Check how the target map format stores
    and interprets texture axes.
-   If implementing a UV-vector editor similar to Hammer++, trace how
    the UI edits the vectors, how face data is updated, and how the
    result is serialized to `.map`/RMF/BSP as applicable.
-   Preserve existing face/world alignment behavior unless the requested
    change explicitly modifies it.
-   Add or update tests for axis direction, face orientation, and
    round-trip serialization where the repository has suitable test
    infrastructure.

### Geometry and map formats

-   Be careful with plane winding, face normals, vertex ordering,
    texture axes, and coordinate-system conventions.
-   Avoid changing geometry or format serialization based only on
    viewport appearance.
-   For format bugs, compare input data, parsed in-memory objects, and
    written output. Include round-trip tests when practical.
-   Never silently drop unknown or unsupported data during load/save
    unless that behavior is already intentional and documented.

## UI, tools, and viewport interaction

-   Follow the existing UI toolkit and editor command patterns; do not
    introduce a replacement UI framework for a small feature.
-   For toolbar, icon, selection, keyboard, or mouse changes, inspect
    the relevant tool state machine and event routing first.
-   Preserve undo/redo behavior and document dirty-state tracking for
    edits.
-   Check both orthographic and 3D viewports when a change affects
    camera controls, selection, overlays, or rendering.
-   For icons, confirm the actual supported formats and resource-loading
    path before adding SVG or PNG support. Do not assume a file
    extension alone makes a format supported.
-   Keep UI interactions responsive and avoid blocking the main thread
    with expensive parsing or rendering operations.

## RMF and other file-loading performance work

-   Trace the complete open/load pipeline before optimizing: file
    selection, parsing, object construction, resource resolution,
    UI/document creation, and viewport refresh.
-   Separate time spent in disk I/O, parsing, allocations, resource
    loading, and UI updates when profiling.
-   Prefer Visual Studio CPU Usage / Instrumentation and memory tools,
    or Windows Performance Recorder/Analyzer when deeper system-level
    traces are needed.
-   Use representative large files and compare before/after
    measurements. Record the test file, build configuration, elapsed
    time, and any relevant memory/allocation data.
-   Do not change file-format behavior or skip validation just to
    improve benchmark results.
-   Treat malformed-file errors as correctness issues first. For errors
    such as an unexpected record/class at a byte offset, inspect the
    parser and format structure rather than adding a blind fallback.

## Testing expectations

For each change, consider:

-   Does the solution compile?
-   Does the affected feature work in the intended GoldSrc workflow?
-   Are existing map files still loaded and saved correctly?
-   Are undo/redo and dirty-state behavior preserved?
-   Are orthographic and 3D viewports unaffected where relevant?
-   Does the change introduce new allocations, blocking operations, or
    resource leaks?
-   Are error messages actionable for invalid input?

If there is no automated test for a behavior, describe a concise manual
test plan.

## Git and patch-file output

When the user asks for a `.patch` file:

1.  Make only the requested changes.
2.  Inspect `git diff --check` and `git diff`.
3.  Generate a patch from the intended changes, for example:
    -   `git diff --binary > feature-name.patch`
    -   If only selected files should be included, stage only those
        files and use `git diff --cached --binary > feature-name.patch`.
4.  Do not include unrelated user changes in the patch. If the working
    tree already contains unrelated modifications, isolate the requested
    changes carefully or explain why a clean patch cannot safely be
    produced.
5.  Verify the patch is non-empty and inspect its file list and header.
6.  If possible, validate it against a clean worktree or temporary
    branch using `git apply --check feature-name.patch`.
7.  Provide the patch file itself and a short summary of changed files,
    build/test results, and any caveats.

Never claim a patch was created or validated unless the file exists and
the relevant command succeeded.

## Scope and communication

-   Do not make unrelated cleanup changes.
-   Do not silently change project-wide frameworks, rendering backends,
    dependencies, or serialization behavior.
-   If the request is ambiguous, inspect the code and state a reasonable
    interpretation; ask only when the ambiguity could lead to materially
    different implementations.
-   Explain technical choices in plain language, including what was
    changed, why, and how it was verified.
-   When uncertain about a legacy behavior, document the uncertainty and
    investigate rather than inventing an answer.
