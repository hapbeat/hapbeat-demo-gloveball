# Hapbeat GloveBall Demo

Meta の Ultimate Glove Ball アリーナ資産を使った、Meta Quest 向けの 1 人プレイ Hapbeat デモ。source repository の checkpoint `69f4c19` から、Unity プロジェクトの再現可能なソーススナップショットを収録している。

## 必要な環境

- Git と [Git Large File Storage (Git LFS)](https://git-lfs.com/)
- Unity Hub
- Unity **6000.0.59f2**（別バージョンへ自動更新しない）
- Unity 6000.0.59f2 の Android Build Support
  - Android Software Development Kit (SDK) & Native Development Kit (NDK) Tools
  - OpenJDK
- OpenXR 対応の Meta Quest 2 / 3 / 3S（実機確認時）

Unity Package Manager はプロジェクトを開くと `Packages/manifest.json` から Universal Render Pipeline、Input System、XR Interaction Toolkit、XR Plug-in Management、OpenXR などを復元する。Meta XR SDK は使用しない。

GloveBall は `Resources/HapbeatDemoSwitchSettings.asset` が存在するため、前面時だけ UDP 7710 の Demo Switch receiver を起動する。current demo ID は `gloveball`、target allowlist には `handdemo`（`com.Hapbeat.HapticHandDemo_G2`）を設定済み。shared secret が空の asset は isolated-LAN unsigned mode で警告を出すため、運用前に全 APK と M5 controller へ同じ secret をローカル設定し、その値を commit しない。

## APK

ビルド済みの Quest 用 APK は [GitHub Releases](https://github.com/hapbeat/hapbeat-demo-gloveball/releases) で配布する。ソースからビルドする場合は以下の手順に従う。

## 初回セットアップ

1. リポジトリを clone し、LFS を有効化して大容量アセットを取得する。

   ```powershell
   git lfs install
   git clone https://github.com/hapbeat/hapbeat-demo-gloveball.git
   cd hapbeat-demo-gloveball
   git lfs pull
   ```

2. Unity Hub でこのディレクトリを Unity 6000.0.59f2 のプロジェクトとして開き、package import と script compilation の完了を待つ。Hapbeat SDK（`com.hapbeat.sdk`）と Demo Switch（`com.hapbeat.demo-switch`）は `Packages/manifest.json` に固定した公開 Git URL から自動で取得される（詳細は [Packages/hapbeat-sdk-source.md](Packages/hapbeat-sdk-source.md)）。
3. Project Settings > Player > Other Settings > Active Input Handling が **Input System Package (New)** のみになっていることを確認する。Both と Input Manager (Old) は使用しない。
4. Android を active build target にし、Project Settings > XR Plug-in Management > Android で OpenXR が有効であることを確認する。Quest Pro 向け eye tracking requirement は有効化しない。

### 手のモデルについて

Volley シーンのハンドトラッキング表示には、Unity XR Hands の HandVisualizer サンプルの手メッシュを使っていた。このメッシュは Unity Package Distribution License によりビルド済みアプリのバイナリとしてのみ再配布でき、ソースアセットとしては公開できないため、このリポジトリには含めていない。

clone したプロジェクトでは、カプセルで構成した Hapbeat 製のプレースホルダーの手（`Assets/GloveBallDemo/Art/FallbackHands/`）が実行時に自動で使われる。関節名は XR Hands と同じなので、指の動き・ボールの当たり判定（手の関節から算出）は同じように動作し、見た目だけが簡素になる。`Assets/HapbeatPrivate/Resources/HapbeatPrivate/UnityHands/` に手メッシュを置くと、`VolleyHandModelResolver` がそちらを優先して使う（このフォルダは git 管理外）。

### Hapbeat ワークスペースで開発する場合

Hapbeat の開発ワークスペース内でこのプロジェクトを使う場合は、Unity Editor を閉じた状態で次を一度実行する。

```powershell
.\tools\link-workspace.ps1
```

非公開アセット（`Assets/HapbeatPrivate`）と、SDK / Demo Switch の作業中ソース（`Packages/` 以下の embedded package）へのディレクトリジャンクションを作成する。リンク先が存在しないものは作らない。何度実行してもよい。

## 安全な検証とビルド

Unity Editor を閉じてから、プロジェクトルートで次の順に実行する。

```powershell
.\tools\run-unity.ps1 -Method GloveBallDemo.Editor.BatchOps.CompileCheck
.\tools\run-unity.ps1 -RunTests EditMode
.\tools\run-unity.ps1 -Method GloveBallDemo.Editor.BatchOps.ReportSceneHealth -ExtraArgs @('-gbScene','Assets/GloveBallDemo/Scenes/Demo.unity')
.\tools\run-unity.ps1 -Method GloveBallDemo.Editor.BatchOps.BuildApk
```

Unity Hub の標準配置以外へインストールした場合は、各コマンドへ Unity 6000.0.59f2 の実行ファイルを明示する。

```powershell
.\tools\run-unity.ps1 -Method GloveBallDemo.Editor.BatchOps.CompileCheck -UnityExe 'D:\Unity\6000.0.59f2\Editor\Unity.exe'
```

移管時に checked-in された Scene、Prefab、Input Actions、Hapbeat EventMap が現在のスナップショットの正本であり、セットアップや通常検証では削除・再生成しない。`BatchOps.BuildDemo` は破壊的な maintenance-only コマンドのため、新たな作業指示なしに実行しない。自動検証では `-gbHapticsLive` を付けず、PC の音声と Hapbeat 実機への触覚送信を無効のままにする。APK の Quest 実機での起動、フレームレート、操作、触覚は自動検証とは別に確認する。

## 別 PC での再現

1. Unity 6000.0.59f2 と同版の Android Build Support 一式を Unity Hub で導入する。
2. このリポジトリを clone し、`git lfs install` と `git lfs pull` を実行する。
3. 初回 import 後、compile check、EditMode tests、scene health、APK build の順に実行する。`BuildDemo` は実行しない。
4. `git status --short` で、意図しない ProjectSettings や生成 asset の差分が残っていないことを確認する。

第三者アセットのライセンスは [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) を参照する。
