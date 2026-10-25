# GPU-Backed Vulkan CI Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a deliberately dispatched GPU-backed Vulkan validation job using the repository's `vulkan-gpu` self-hosted Linux runner and headless harness without queuing routine CI when the runner is absent.

**Architecture:** Extend `.github/workflows/ci.yml` with a separate `vulkan-gpu` job that depends on `build-test` and is enabled only when a maintainer manually dispatches with `gpu_validation: true`. The job builds the existing Vulkan Mesh Demo and runs the existing harness, requiring validation-layer confirmation, screenshot verification, and three rendered content clusters; runner-wide graphics dependencies are preinstalled and are not provisioned by CI.

**Tech Stack:** GitHub Actions YAML, .NET 10, Bash headless Vulkan harness, Weston headless compositor, Vulkan validation layers.

## Global Constraints

- Use runner labels `[self-hosted, linux, vulkan-gpu]` exactly.
- Do not execute the GPU job on `pull_request` events.
- Run the GPU job only for `workflow_dispatch` when input `gpu_validation` is true; no push or pull request schedules it.
- Keep the normal hosted `build-test` job unchanged and require it to pass before starting GPU validation.
- Use `samples/Volt.VulkanMeshDemo` and `tools/test-vulkan-headless.sh`; do not implement a second harness.
- Require `--validation`, `--screenshots`, and `--expect-clusters 3`.
- Do not install or modify runner-wide system packages in the workflow.
- Do not add artifact-upload behavior; harness run artifacts remain on the runner.

---

## File Structure

- Modify `.github/workflows/ci.yml`: add one GPU job with an event guard, runner labels, dependency, .NET setup, build and harness invocation.
- No source code, test code, dependencies, or harness changes are needed.

---

### Task 1: Add the guarded GPU-backed Vulkan job

**Files:**
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: existing workflow triggers, `build-test` job, `samples/Volt.VulkanMeshDemo`, and `tools/test-vulkan-headless.sh` command-line interface.
- Produces: a `vulkan-gpu` job that runs only for push/manual-dispatch events and whose success means the existing harness verified the bounded render, validation, and screenshot cluster assertion.

- [ ] **Step 1: Add the GPU job after `build-test`**

Add a boolean `gpu_validation` input to `workflow_dispatch` with default `false`, then add a job with this shape, preserving the workflow's push and pull-request triggers and leaving `build-test` unchanged:

```yaml
  vulkan-gpu:
    name: GPU-backed Vulkan validation
    if: github.event_name == 'workflow_dispatch' && inputs.gpu_validation == true
    needs: build-test
    runs-on: [self-hosted, linux, vulkan-gpu]
    timeout-minutes: 15
    steps:
      - name: Check out the repository
        uses: actions/checkout@v4

      - name: Install the .NET SDK
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Restore
        run: dotnet restore Volt.slnx

      - name: Build the Vulkan mesh demo
        run: dotnet build samples/Volt.VulkanMeshDemo -c Release --no-restore

      - name: Run headless Vulkan validation
        run: >-
          ./tools/test-vulkan-headless.sh
          --project samples/Volt.VulkanMeshDemo
          --frames 600
          --validation
          --screenshots
          --expect-clusters 3
```

The event guard limits this self-hosted runner job to a deliberate manual dispatch. The `needs` dependency ensures GPU capacity is not used when standard CI fails. The sample build invokes the project shader target, so the runner's documented `glslangValidator` prerequisite must already be present alongside the headless harness dependencies. If no matching runner is registered, the manually requested hardware run may queue, but ordinary commits will not create one.

- [ ] **Step 2: Review the workflow diff**

Run: `git diff --check && git diff -- .github/workflows/ci.yml`

Expected: the GPU input and gated job are added; push/pull-request triggers and hosted CI steps remain unchanged; no whitespace errors.

- [ ] **Step 3: Validate workflow syntax and event/job behavior**

Run `actionlint .github/workflows/ci.yml` if `actionlint` is available. Also inspect the resulting workflow to verify all of the following: the boolean dispatch input defaults to false, the `if` condition requires a deliberate dispatch with the input true, push and pull-request events cannot schedule it, the runner labels are exact, `needs: build-test` is present, and the job commands pass the validation, screenshot, and three-cluster options. If `actionlint` is unavailable, use an available YAML parser to parse the file and perform these structural checks; do not claim syntax validation passed if no parser can be run.

- [ ] **Step 4: Run the headless harness self-tests**

Run: `./tools/tests/test-vulkan-headless.sh`

Expected: all harness self-tests pass. Then run `bash -n tools/test-vulkan-headless.sh` as a syntax smoke check. These local checks validate the harness contract, not the actual GPU job.

- [ ] **Step 5: Record the runner-only verification limitation**

The actual `vulkan-gpu` job requires the configured self-hosted runner and its preinstalled Weston 15/headless GL, `weston-screenshooter`, Python 3, `glslangValidator`, .NET 10 and Vulkan GPU/validation-layer runtime. Do not attempt to simulate GPU success on a non-GPU host. Report that end-to-end job execution remains pending until GitHub Actions schedules it on that runner.

---

## Self-Review

- **Spec coverage:** Uses the approved runner labels, deliberate manual-only execution, hosted-job dependency, existing demo and harness, validation and screenshot checks, three content clusters, preinstalled dependencies, and no artifact-upload changes.
- **Placeholder scan:** No TODO/TBD or unspecified implementation steps. Verification tools are conditional on local availability, with explicit reporting requirements.
- **Type/interface consistency:** The job name/ID and the existing `build-test` job ID are valid GitHub Actions job references. Harness flags match the documented CLI.
- **Scope:** One CI workflow modification; no independent subsystem or implementation tasks are warranted.
