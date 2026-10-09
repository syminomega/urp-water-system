# Changelog

## [2.1.1-preview.1] — 2026-10-08 — Unity 6000.6 / URP 17.6

- Fix compilation after the removal of legacy `Configure` and `Execute` render pass APIs; use Render Graph on newer Unity versions.
- Use `GetEntityId()` and full `EntityId` keys for buoyancy jobs, while retaining instance IDs on older Unity versions.
- Update buoyancy Rigidbody damping properties to the current Unity API, retaining legacy properties on older Unity versions.
- Verified with Unity 6000.6.4f1 / URP 17.6.0, including the water test scene.

## Render Graph update — Unity 6000.3 / URP 17

- Add Render Graph implementations for water effects and caustics, and update water shaders for URP 17 APIs.
- Improve Water FX texture allocation and cleanup with RTHandle management.
- Isolate the water depth camera from post-processing, volumes, anti-aliasing and XR.
- Tested with Unity 6000.3.

## Original — Unity 2022 / URP 12

- Fork the BoatAttack water system with water shaders, Gerstner waves, buoyancy jobs, planar reflections, water effects and caustics.
- Use the legacy URP rendering path, supporting Unity versions up to 2022 and URP 12.
