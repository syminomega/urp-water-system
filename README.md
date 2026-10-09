# URP Water System

This is a modified version of `com.verasl.water-system` from Unity's official [BoatAttack](https://github.com/Unity-Technologies/BoatAttack) project.

## Differences from the original fork

- Add Unity 6 / URP 17 Render Graph support for water effects and caustics, retaining the legacy rendering path.
- Improve Water FX texture lifetime management with RTHandles.
- Update water shader APIs and isolate the water depth camera from URP volumes and post-processing.
- Fix Unity 6000.6 compatibility, including removed render pass APIs and full `EntityId` support for buoyancy jobs.

See [CHANGELOG.md](CHANGELOG.md) for the three compatibility milestones.
