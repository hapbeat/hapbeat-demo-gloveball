# Where the Hapbeat packages come from

`manifest.json` is meant to pin both Hapbeat packages to public Git sources, so a plain clone
restores them without any neighbouring checkout:

```json
"com.hapbeat.sdk": "https://github.com/hapbeat/hapbeat-unity-sdk.git#v0.5.0",
"com.hapbeat.demo-switch": "https://github.com/hapbeat/hapbeat-demos.git?path=unity/packages/com.hapbeat.demo-switch#75eb51687e35bd88af61cc181fa6bd5dcd0b4753"
```

- `com.hapbeat.sdk` currently points at the workspace checkout instead
  (`"file:../../../../repos-sdk/hapbeat-unity-sdk"`, as Boxing and Energy Duel do): the devices run firmware
  that accepts only the v2 stream (DEC-074), and v2 support with the per-device legacy fallback (DEC-075) is not
  in any released tag yet (`v0.5.0` sends the pre-v2 stream, which that firmware ignores). Pin it back to a
  release tag once the SDK with the v2 stream (0.6.0) is released. Tags before `v0.5.0` are not usable:
  `v0.4.0`'s editor assembly calls `EditorUtility.EntityIdToObject`, which does not exist in Unity 6000.0 LTS.
- `com.hapbeat.demo-switch` has no release of its own; it is meant to be pinned to an immutable commit of the
  `hapbeat-demos` repository (currently `file:../../packages/com.hapbeat.demo-switch`).

`packages-lock.json` is not committed: it records whichever source a checkout resolved (Git URL or an
embedded package below), so it differs between a plain clone and a workspace checkout.

## Live sources in the Hapbeat workspace

`tools/link-workspace.ps1` creates directory junctions `Packages/com.hapbeat.sdk` and
`Packages/com.hapbeat.demo-switch` pointing at the workspace checkouts. Unity treats a package folder
inside `Packages/` as an embedded package, which overrides the Git URL above, so SDK changes are picked up
without editing the manifest. Both junctions are git-ignored. Embedded packages are testable, so the SDK's
own EditMode tests also run in that setup.
