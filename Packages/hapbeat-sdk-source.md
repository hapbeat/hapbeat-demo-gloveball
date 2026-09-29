# Where the Hapbeat packages come from

`manifest.json` pins both Hapbeat packages to public Git sources, so a plain clone
restores them without any neighbouring checkout:

```json
"com.hapbeat.sdk": "https://github.com/hapbeat/hapbeat-unity-sdk.git#v0.5.0",
"com.hapbeat.demo-switch": "https://github.com/hapbeat/hapbeat-demos.git?path=unity/packages/com.hapbeat.demo-switch#75eb51687e35bd88af61cc181fa6bd5dcd0b4753"
```

- `com.hapbeat.sdk` uses the release tag `v0.5.0`, the newest published tag this project compiles and
  passes its tests with on Unity 6000.0.59f2. Tags before `v0.5.0` are not usable: `v0.4.0`'s editor assembly
  calls `EditorUtility.EntityIdToObject`, which does not exist in Unity 6000.0 LTS; `v0.5.0` routes it through
  the version-guarded `HapbeatEditorCompat` helper.
- `com.hapbeat.demo-switch` has no release of its own; it is pinned to an immutable commit of the
  `hapbeat-demos` repository.

`packages-lock.json` is not committed: it records whichever source a checkout resolved (Git URL or an
embedded package below), so it differs between a plain clone and a workspace checkout.

## Live sources in the Hapbeat workspace

`tools/link-workspace.ps1` creates directory junctions `Packages/com.hapbeat.sdk` and
`Packages/com.hapbeat.demo-switch` pointing at the workspace checkouts. Unity treats a package folder
inside `Packages/` as an embedded package, which overrides the Git URL above, so SDK changes are picked up
without editing the manifest. Both junctions are git-ignored. Embedded packages are testable, so the SDK's
own EditMode tests also run in that setup.
