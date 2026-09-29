# Third-party notices

This file was prepared from source checkpoint `69f4c19`, embedded font metadata, and the Unity package versions pinned in `Packages/manifest.json`.

## Ultimate Glove Ball (Meta Platforms, Inc.)

The `Assets/UltimateGloveBall/` snapshot contains assets copied or adapted from the Meta open-source sample [Unity-UltimateGloveBall](https://github.com/oculus-samples/Unity-UltimateGloveBall) under its upstream MIT license. The obsolete `Assets/UltimateGloveBall/Shader/ProceduralGradientSkybox.shader` and its `.meta` file are excluded from the snapshot; the replacement, `Assets/GloveBallDemo/Shaders/ProceduralGradientSkybox-codex.shader`, is Hapbeat-authored.

The source project's notice supplies the following upstream MIT license verbatim:

```text
MIT License

Copyright (c) Meta Platforms, Inc. and its affiliates.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Fonts and generated SDF assets (SIL Open Font License 1.1)

The snapshot retains these fonts and their generated signed distance field (SDF) font assets, atlases, and materials:

- **Rubik Medium** — embedded font metadata: `Copyright (c) 2015 by Hubert & Fischer. All rights reserved.` and `This Font Software is licensed under the SIL Open Font License, Version 1.1.`
- **Bungee Regular** — embedded font metadata: `Copyright 2008 The Bungee Project Authors (david@djr.com)` and `SIL Open Font License, Version 1.1.`
- **Liberation Sans** — the adjacent source license records `Digitized data copyright (c) 2010 Google Corporation` and `Copyright (c) 2012 Red Hat, Inc.` under the SIL Open Font License, Version 1.1.

The complete license text is distributed as [SIL-Open-Font-License-1.1-codex.txt](third-party-licenses/SIL-Open-Font-License-1.1-codex.txt). Copies of the fonts and derivative SDF assets must retain the copyright and license notices, must remain under the SIL Open Font License 1.1, and must not be sold by themselves. A modified font must not use a Reserved Font Name without the corresponding copyright holder's permission. Copyright-holder and author names may be used only to acknowledge contributions unless permission is granted.

## XR Interaction Toolkit 3.0.8 sample

`Assets/Samples/XR Interaction Toolkit/3.0.8/XR Device Simulator/` is retained from XR Interaction Toolkit 3.0.8, copyright © 2025 Unity Technologies. Unity's package license states that it is licensed under the **Unity Companion License for Unity-dependent projects**. It may therefore be used and redistributed only as part of a project that depends on and is used with the Unity Engine, subject to the full Unity Companion License terms; it is supplied on an AS IS basis.

- [XR Interaction Toolkit 3.0.8 package license](third-party-licenses/XR-Interaction-Toolkit-3.0.8-LICENSE-codex.md)
- [XR Interaction Toolkit 3.0.8 third-party notices](third-party-licenses/XR-Interaction-Toolkit-3.0.8-Third-Party-Notices-codex.md) — includes the Apache License 2.0 notice for gesture code from the ARCore Unity Object Manipulation example, copyright © 2017 Google Inc.

## TextMesh Pro Essential Resources / Unity UI 2.0.0

The imported TextMesh Pro Essential Resources were verified against `Package Resources/TMP Essential Resources.unitypackage` in the resolved `com.unity.ugui` 2.0.0 package. Unity UI is copyright © 2015-2020 Unity Technologies ApS and is licensed under the **Unity Companion License for Unity-dependent projects**. These resources may therefore be used and redistributed only as part of a project that depends on and is used with the Unity Engine, subject to the full Unity Companion License terms; they are supplied on an AS IS basis. Liberation Sans and its generated SDF assets additionally remain subject to the SIL Open Font License 1.1 above.

- [Unity UI 2.0.0 package license](third-party-licenses/Unity-UI-2.0.0-LICENSE-codex.md)

## XR Interaction Toolkit 3.3.1 Hands Interaction Demo (ghost hand materials and shaders)

`Assets/GloveBallDemo/Art/UnityGhostHands/Materials/`, `Shaders/` and `Textures/`, and the derived `VolleySkinHand.mat`, come from the Hands Interaction Demo sample of XR Interaction Toolkit 3.3.1, copyright © 2025 Unity Technologies, licensed under the **Unity Companion License for Unity-dependent projects**. The package license and third-party notices are kept next to the materials (`Materials/LICENSE.md`, `Materials/Third Party Notices.md`); see also [NOTICE.md](Assets/GloveBallDemo/Art/UnityGhostHands/NOTICE.md).

## Unity XR Hands sample hand meshes (not redistributed)

The XR Hands HandVisualizer sample meshes are licensed under the Unity Package Distribution License, which allows distribution only inside built application binaries. They are not included in this repository. A clone of this repository uses the Hapbeat-authored placeholder hands in `Assets/GloveBallDemo/Art/FallbackHands/` (generated from capsules by `Assets/Editor/VolleyPlaceholderHandBuilder.cs`). Builds made with a private copy of the meshes linked into `Assets/HapbeatPrivate/` include them as binary content only.
