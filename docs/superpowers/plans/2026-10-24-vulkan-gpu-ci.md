# GPU-Backed Vulkan CI Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a push/manual-only GPU-backed Vulkan validation job using the repository's `vulkan-gpu` self-hosted Linux runner and headless harness.

**Architecture:** Extend `.github/workflows/ci.yml` with a separate `vulkan-gpu` job that depends on `build-test`, explicitly skips pull-request events, and runs the existing Vulkan Mesh Demo through the existing headless harness. The job restores/builds the sample and requires validation-layer confirmation, a passing screenshot verification, and three rendered content clusters; runner-wide graphics dependencies are preinstalled and are not provisioned by CI.

**Tech Stack:** GitHub Actions YAML, .NET 10, Bash headless Vulkan harness, Weston headless compositor, Vulkan validation layers.

## Global Constraints

- Use runner labels `[self-hosted, linux, vulkan-gpu]` exactly.
- Do not execute the GPU job on `pull_request` events.
- Run the GPU job only for the workflow's existing push branches and `workflow_dispatch`.
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

Add a job with this shape, preserving the workflow's current push, pull-request, and dispatch triggers and leaving `build-test` unchanged:

```yaml
  vulkan-gpu:
    name: GPU-backed Vulkan validation
    if: github.event_name != 'pull_request'
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

The event guard is required because the workflow also handles pull requests; it limits this self-hosted runner job to pushes and manual dispatch. The `needs` dependency ensures GPU capacity is not used when standard CI fails. The sample build invokes the project shader target, so the runner's documented `glslangValidator` prerequisite must already be present alongside the headless harness dependencies.

- [ ] **Step 2: Review the workflow diff**

Run: `git diff --check && git diff -- .github/workflows/ci.yml`

Expected: only the new GPU job is added; existing trigger definitions and hosted CI steps remain unchanged; no whitespace errors.

- [ ] **Step 3: Validate workflow syntax and event/job behavior**

Run `actionlint .github/workflows/ci.yml` if `actionlint` is available. Also inspect the resulting workflow to verify all of the following: the `if` condition excludes pull requests, the runner labels are exact, `needs: build-test` is present, and the job commands pass the validation, screenshot, and three-cluster options. If `actionlint` is unavailable, use an available YAML parser to parse the file and perform these structural checks; do not claim syntax validation passed if no parser can be run.

- [ ] **Step 4: Run the headless harness self-tests**

Run: `./tools/tests/test-vulkan-headless.sh`

Expected: all harness self-tests pass. Then run `bash -n tools/test-vulkan-headless.sh` as a syntax smoke check. These local checks validate the harness contract, not the actual GPU job.

- [ ] **Step 5: Record the runner-only verification limitation**

The actual `vulkan-gpu` job requires the configured self-hosted runner and its preinstalled Weston 15/headless GL, `weston-screenshooter`, Python 3, `glslangValidator`, .NET 10 and Vulkan GPU/validation-layer runtime. Do not attempt to simulate GPU success on a non-GPU host. Report that end-to-end job execution remains pending until GitHub Actions schedules it on that runner.

---

## Self-Review

- **Spec coverage:** Uses the approved runner labels, push/manual-only execution, hosted-job dependency, existing demo and harness, validation and screenshot checks, three content clusters, preinstalled dependencies, and no artifact-upload changes.
- **Placeholder scan:** No TODO/TBD or unspecified implementation steps. Verification tools are conditional on local availability, with explicit reporting requirements.
- **Type/interface consistency:** The job name/ID and the existing `build-test` job ID are valid GitHub Actions job references. Harness flags match the documented CLI.
- **Scope:** One CI workflow modification; no independent subsystem or implementation tasks are warranted.
