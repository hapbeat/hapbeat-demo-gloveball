# Unity ghost hand assets

These are Unity sample assets, not Hapbeat-authored or CC0 assets.

- `Models/LeftHand.fbx`, `RightHand.fbx`: unchanged copies from HandDemo's XR Hands 1.7.1 HandVisualizer sample. The installed XR Hands 1.7.3 has matching model GUIDs. XR Hands copyright © 2024 Unity Technologies; Unity Package Distribution License: https://unity.com/legal/licenses/unity-package-distribution-license . This permits use in Unity project content and distribution of integrated binaries; it is not permission to publish a standalone raw-asset library.
- `Materials`, `Shaders`, `Textures`: Hands Interaction Demo from XR Interaction Toolkit 3.3.1. XR Interaction Toolkit copyright © 2025 Unity Technologies; Unity Companion License: https://unity.com/legal/licenses/unity-companion-license . Retain this notice and the associated license information. The current package copy of the combined finger texture is included alongside HandDemo's older texture because the material contains references to both GUIDs.
- Runtime joint motion uses the project's `com.unity.xr.hands` package, not copied interaction scripts. The ghost visual intentionally excludes the sample's poking, grabbing, menus and locomotion components.

Use requires a valid Unity engine license and compliance with the applicable third-party notices. Private/non-distributed status alone does not grant rights to arbitrary assets. Do not publish the raw XR Hands models as independently reusable assets. These files were added for the user's own Unity demonstration project; no public upload was performed.
