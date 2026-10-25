# GPU-Backed Vulkan CI Validation Design

## Goal

Add an opt-in GPU-backed Vulkan validation job to the existing GitHub Actions CI workflow, using the repository's self-hosted Linux runner labeled `vulkan-gpu` and the existing headless Vulkan harness.

## Scope and trigger policy

Keep the job in `.github/workflows/ci.yml`. Run it only for the workflow's existing push branches and `workflow_dispatch`; do not run it on `pull_request`. This avoids executing untrusted pull-request code on the self-hosted runner. The normal hosted build/test job remains unchanged, and the Vulkan job should depend on its success.

## Execution and validation

The GPU job uses the labels `[self-hosted, linux, vulkan-gpu]`, checks out the repository, sets up .NET 10, restores the solution, and builds `samples/Volt.VulkanMeshDemo` in Release mode. It then invokes `tools/test-vulkan-headless.sh` against that sample with Vulkan validation and screenshot verification enabled, requiring three horizontal rendered-content clusters. This checks both validation-layer activation/diagnostics and actual rendered output.

The runner is expected to already provide the headless harness's documented Linux prerequisites, including Weston 15 with its headless GL backend, `weston-screenshooter`, Python 3, .NET 10, and a working Vulkan GPU and validation-layer setup. The workflow will not install or modify runner-wide system packages.

## Failure behavior

The harness's exit code determines job success. A failed build, inactive validation layer, validation diagnostic, frame-count mismatch, screenshot capture/verification error, or incorrect cluster count fails the job. Existing harness artifacts remain available in its output directory for local diagnosis; artifact-upload changes are not included in this design.

## Files

- Modify `.github/workflows/ci.yml` to add the guarded GPU job.
- Add this design document for review before implementation planning.

## Verification

Review the workflow's event guards and job dependency, validate its YAML syntax, and run the headless harness self-tests (`./tools/tests/test-vulkan-headless.sh`) where dependencies permit. The GPU-specific end-to-end job can only be verified on a runner that has the `vulkan-gpu` label and the documented graphics prerequisites.
