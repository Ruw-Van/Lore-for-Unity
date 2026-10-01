# Lore for Unity --- Technical Specification

**Q1--Q16 統合仕様 / Specification Freeze**\
作成時点: 2026-10-01

> 本書は、これまでの設計QAで確定した内容を実装用仕様として統合したものです。後から明示的に修正された決定を優先し、途中案（例:
> UI Toolkit Primary / IMGUI Primary）は正式仕様から除外しています。

## 1. 製品定義

Lore for Unity は、Lore を Unity Editor から安全かつ Unity の Asset
モデルに適した形で利用する Editor Integration とする。Lore の VCS
ロジック自体は再実装しない。

-   Plugin の責任: Unity Editor integration、Asset/.meta 正規化、Status
    表示、Check In、Lock、Scene/Prefab safety、Working-copy
    safety、Conflict UX、Runtime management、Diagnostics、Recovery。
-   Lore の責任:
    Repository、Revision、Branch、Status、Lock、Merge/Conflict 等の VCS
    Source of Truth。
-   Player Build には Plugin、Lore SDK/CLI、Runtime Manager、Repository
    情報を含めない。Editor-only とする。

最上位原則は、**Lore が既に責任を持つ情報・処理は Lore に任せ、Unity
固有の Asset モデル・Editor 状態・安全性・UX のみを Lore for Unity
が追加する**こと。

## 2. Repository / Asset モデル

### 2.1 Repository identity

Lore が持つ Repository ID を正式な identity とし、Unity 側で独自 ID
は生成しない。Project path は location 情報であり identity ではない。

### 2.2 Unity Asset identity

Unity Asset は GUID
を中心に識別し、`GUID → Asset path → Repository path`
へ解決する。Move/Rename 後も GUID が維持される限り同一 Asset とみなす。

### 2.3 Asset と `.meta`

Asset 本体と `.meta` は Lore 内部では個別状態を保持するが、Unity UI では
Logical Asset として集約する。

``` text
Foo.prefab
Foo.prefab.meta
    ↓
UnityLogicalAsset(Foo.prefab)
```

`.meta` のみ変更された場合も Logical Asset は Modified
と表示し、詳細では実変更を確認できる。

### 2.4 Repository File

`ProjectSettings/`、`Packages/manifest.json`、`Packages/packages-lock.json`
等は Unity GUID Asset ではなく Repository File として扱う。

## 3. Status / Change Detection

``` text
Unity callback
↓
Change hint
↓
Queue / deduplicate / debounce
↓
Lore status query
↓
immutable StatusSnapshot
↓
UI
```

-   Unity callback は通知源であり Source of Truth ではない。
-   Project Window / `OnGUI` の描画中に Lore query、disk/network I/O
    を行わない。
-   `StatusSnapshot` は atomic replace。
-   Refresh は Targeted / Repository / Remote を使い分ける。
-   古い read refresh は可能なら cancel。cancel 不可なら Generation
    で古い結果を publish しない。
-   Lookup index は RepositoryPath / Unity GUID / Unity Asset path /
    Folder aggregate を持つ。

## 4. Lock Policy

  対象                   Default Auto Lock
  ---------------------- -------------------
  Scene                  ON
  ProjectSettings        ON
  Non-mergeable asset    ON
  Script                 OFF
  Mergeable text asset   OFF

編集 intent 検出時に Lock を取得する。同一 Asset への request は
single-flight。Project Settings で policy を変更可能。

## 5. Check In / Push

``` text
Selection
→ Logical Asset resolution
→ asset/.meta expansion
→ targeted status refresh
→ Unity validation
→ Lore validation
→ stage
→ stage verification
→ create revision
→ repository/status refresh
→ optional Push
```

Check In 後 Push は Default ON。ただし Commit と Push
は別結果として扱い、**Commit 成功 / Push 失敗**を正確に表現する。Retry
は新 Operation とする。

Working Copy を変更する操作は同一 Repository 内で原則直列化する。安全な
Read は必要に応じて並列化可能。

## 6. Branch / Sync / Merge / Conflict

Switch / Sync / Merge は Lore 処理だけで成功扱いにせず、AssetDatabase
refresh、Unity import、Scene/Prefab state、validation、Status refresh
まで完了して成功とする。

Unsaved Scene / Prefab Mode
等を事前確認し、未保存編集を勝手に破棄しない。

Conflict の Source of Truth は Lore native state。Plugin 独自の Conflict
DB は作らない。

Resolver chain:

``` text
Unity Structured Resolver
↓
Configured external merge tool
↓
Text resolver
↓
Binary choose-version resolver
```

Diff mode は切替可能。**標準は 2: 単純比較**。Structured Diff
は利用可能でも Default にしない。

## 7. Operation / Error / Recovery

重要 Operation に `OperationId`
を付与し、UI、Logging、Diagnostics、Recovery Journal で共有する。

``` text
Preparing
→ Executing
→ WaitingForUnity
→ Validating
→ Finalizing
→ Completed / Failed / Cancelled
```

Operation 開始前に Backend を resolve して固定し、途中で SDK → CLI
fallback は行わない。副作用発生後の retry は状態を再取得した新 Operation
とする。

代表 Error:

-   NetworkUnavailable
-   AuthenticationRequired
-   Conflict
-   Locked
-   InvalidRepository
-   UnsupportedOperation
-   RuntimeMissing
-   RuntimeCorrupted
-   VersionMismatch
-   ValidationFailed
-   Unknown

Working Copy を大きく変更する操作では persistent Recovery Journal
を使用する。Phase boundary ごとに atomic write し、Domain Reload /
Editor crash 後に状態を再構築できるようにする。秘密情報は保存しない。

## 8. Backend Architecture

Primary backend は Lore C# SDK。Operation ごとの Capability に応じて CLI
fallback。

``` text
Capability Resolver
↓
Lore C# SDK
↓ operation-specific fallback
Lore CLI
↓
Unsupported
```

Backend は God Interface にせず、Status / Revision / Branch / Lock /
Diff / Merge / Repository 等に分割する。

Lore SDK 型を UI/Application へ漏らさず Infrastructure Adapter で Domain
型へ変換する。

## 9. Lore Runtime 配布・管理

### 9.1 Package と Runtime の分離

Lore binary は Plugin Git Repository に格納しない。Git LFS も不要。

Plugin 内 Runtime Manifest に以下を固定する。

``` text
LoreVersion
Platform
OfficialArtifactUrl
SHA-256
DownloadSize
ArtifactFormat
```

取得元は Epic Games 公式 Artifact。

### 9.2 Runtime install UX

外部 Runtime は自動ダウンロードしない。

初回または Plugin 更新で要求 Lore Version
が変わった場合、Version、Source=Epic
Games、License=MIT、Platform、Download size を表示し、`Install` /
`Not Now` を提示する。

### 9.3 Runtime cache

-   OS 標準ユーザー領域に固定。保存先変更機能は設けないが Open Folder
    は提供。
-   Lore Version + Platform 単位で複数 Unity Project 間共有。
-   System PATH 上の Lore は使用しない。Diagnostics で検出表示するだけ。
-   自動削除・容量上限なし。
-   Runtime Manager から Verify / Repair / Remove / Remove All Unused。
-   `ProjectId / ProjectPath / RequiredLoreVersion / LastSeen` を永続
    Registry に保持。
-   現在別 Unity Editor / Lore process が使用中の Runtime は削除不可。
-   `Install from File...` によるオフライン導入を提供し、同一 SHA-256
    を要求。

### 9.4 Installation safety

``` text
Acquire cross-process lock
↓
Download or local artifact
↓
SHA-256 verification
↓
Archive validation
↓
Temporary extraction
↓
Expected-file / version probe
↓
Atomic install
↓
Registry update
↓
Release lock
```

失敗時は `Retry` / `Open Official Download Page` /
`Install from File...` を提供。

URL は Runtime Manifest に完全固定し、自動探索や外部 Manifest
更新は行わない。

## 10. Package / Distribution

  項目                   確定仕様
  ---------------------- ---------------------------------
  Repository             GitHub Public Repository
  Package layout         Repository root = UPM Package
  Primary distribution   Unity Package Manager + Git URL
  Versioning             SemVer
  Release ref            Git tag (`v0.1.0` 等)
  Development ref        `main`（動作保証外）
  `.unitypackage`        配布しない
  Registry/OpenUPM       初期必須ではない
  Plugin license         MIT
  Lore runtime           Epic Games 公式 Artifact

`package.json version` / Git tag / GitHub Release version は一致必須。

Plugin 独自の update check は行わず Unity Package Manager に任せる。

## 11. 対応 Platform

初期正式対応:

-   Windows x64
-   macOS arm64

非対応 Platform でも Package 自体は正常に導入・compile 可能とし、Lore
機能のみ Unsupported と表示する。

Plugin Version ごとに検証済み Lore Version を固定する。Lore
の最新版通知は行わず、Plugin の Runtime Manifest を Plugin update
によって明示的に変更する。

## 12. Unity Integration

### 12.1 Editor lifecycle

``` text
InitializeOnLoad
↓
Load settings
↓
Read Runtime Manifest
↓
Check platform/runtime
↓
Create context
↓
Register Unity hooks
↓
Detect repository
↓
Initialize backend
↓
Initial status refresh
```

Runtime 未導入でも Unity Project は正常に開き、Lore UI は Setup Required
状態になる。

### 12.2 Domain Reload / Enter Play Mode

-   Domain Reload ON/OFF の両方に対応。
-   Static state を Source of Truth にしない。
-   Callback 二重登録を防止。
-   Assembly Reload 前に Journal/log を flush。
-   Reload 後は Lore native state を再照会。

### 12.3 Unity API boundary

Lore I/O / network / file scan は Main Thread 外。

Unity API は Main Thread のみ。

Application/Core は AssetDatabase、EditorApplication
等を直接知らない。Integration layer が Working Copy / Main Thread bridge
を提供する。

## 13. UI / Editor UX

**特定の UI framework を Architecture 上の要件・依存としない。**

`UI Toolkit Primary` / `IMGUI Primary` の両案は撤回済み。

通常の Unity Editor 拡張として、機能に適した UnityEditor API
を使用する。

-   EditorWindow
-   EditorGUI / EditorGUILayout
-   SettingsProvider
-   MenuItem / GenericMenu
-   Project Window callbacks
-   その他必要な UnityEditor API

UI から Lore SDK/CLI を直接呼ばない。

``` text
Editor UI
↓
Controller / ViewModel
↓
Application Service
```

### 13.1 Main UI

``` text
Window > Lore
  └─ Lore
     ├─ Changes
     ├─ History
     └─ Branches

Window > Lore
  └─ Diagnostics
```

### 13.2 Keyboard shortcut

**標準キーボードショートカットは無し。**

将来 Shortcut Manager に command を公開しても default binding
は空。危険操作への shortcut は原則提供しない。

### 13.3 Progress / Notification

-   短い read は modal progress を出さない。
-   長い read は inline progress。
-   Write operation は Phase 表示。
-   \% がない場合に偽の進捗率を出さない。
-   通常 refresh / auto-lock 成功では通知しない。
-   Conflict、Lock
    failure、Commit成功/Push失敗など行動が必要な状態を明確に通知する。

## 14. Settings / Secrets / Diagnostics

### 14.1 Settings separation

  領域                              用途
  --------------------------------- ---------------------------------------
  Project Settings                  チーム共有 policy
  User Preferences / UserSettings   UI・通知・個人環境
  Lore native configuration         Lore が所有。重複保存しない
  Runtime Registry                  ユーザー共通 Runtime/Project 利用情報

Credential/token/password は ProjectSettings、UserSettings
asset、Recovery Journal、Diagnostics export、Log に平文保存しない。

### 14.2 Diagnostics

最低限以下を表示可能とする。

-   Plugin / Unity / OS / Architecture version
-   Required / Installed Lore version
-   Runtime health
-   Repository ID / Root / Branch
-   Local / Remote revision
-   Backend capability matrix
-   Status generation / last refresh / stale state
-   Current / last Operation
-   Recovery Journal state
-   Copy Diagnostics（秘密情報除去）

### 14.3 Logging

Unity Console は Warning/Error 中心。

Info/Debug/Trace は Diagnostics log。

OperationId、RepositoryId、Backend 等を structured metadata として扱う。

## 15. Architecture / Assemblies

``` text
Unity Editor UI       Unity Integration
       \                 /
        \               /
           Application
               ↓
              Core
               ↑
        Backend Contracts
          /           \
     Lore SDK        Lore CLI

Runtime Management
       ↓
Epic official artifact
       ↓
User-wide cache
```

禁止依存:

-   Core → UnityEditor / Lore SDK
-   Application → Lore SDK
-   UI → Lore SDK / CLI

Composition Root のみ全層を組み立てる。

### 15.1 Assemblies

-   `Lore.Unity.Core.Editor`
-   `Lore.Unity.Application.Editor`
-   `Lore.Unity.Infrastructure.Editor`
-   `Lore.Unity.Integration.Editor`
-   `Lore.Unity.UI.Editor`
-   `Lore.Unity.Tests.Editor`

### 15.2 DI / Lifetime

外部 DI framework は必須依存にしない。

Composition Root + Constructor Injection を基本とし、Global static
Service Locator は使用しない。

### 15.3 Async

`Task` / `ValueTask` / `CancellationToken` を基本とし、UniTask
を必須依存にしない。

## 16. Core Data / Interfaces

### 16.1 Value Objects

-   RepositoryId
-   RepositoryPath
-   AbsolutePath
-   UnityAssetPath
-   BranchId / BranchName
-   RevisionSignature / RevisionNumber
-   UnityAssetGuid
-   LoreVersion
-   OperationId

OS absolute path / Repository path / Unity Asset path を同じ `string`
として扱わない。

RepositoryPath separator は `/` に正規化。

### 16.2 Repository snapshot

Repository は巨大 Mutable Object にせず immutable `RepositorySnapshot`
として扱う。

### 16.3 File Status

Modified 等の巨大 enum 1個で状態を潰さず、WorkingState / StageState /
LockState / ConflictState / RemoteState を組み合わせて保持する。

### 16.4 Application Services

-   RepositoryService
-   StatusService
-   CheckInService
-   PushService
-   SyncService
-   BranchService
-   MergeService
-   ConflictService
-   LockService
-   DiffService
-   HistoryService
-   RuntimeService
-   RecoveryService

## 17. Runtime Manager クラス構成

``` text
LoreRuntimeManager
├─ RuntimeManifestProvider
├─ RuntimeRegistry
├─ RuntimeDownloader
├─ RuntimeVerifier
├─ RuntimeInstaller
├─ RuntimeUsageRegistry
└─ RuntimeLockManager
```

Repository を開いていなくても Runtime 管理可能。

Install は temporary download/extract → SHA-256 → validation → atomic
move → registry update。

## 18. Package Directory

``` text
package.json
README.md
CHANGELOG.md
LICENSE.md
Third Party Notices.md

Editor/
├─ Core/
│  ├─ Repository/
│  ├─ Status/
│  ├─ Revision/
│  ├─ Branch/
│  ├─ Lock/
│  ├─ Conflict/
│  └─ Operations/
├─ Application/
│  ├─ Status/
│  ├─ CheckIn/
│  ├─ Push/
│  ├─ Sync/
│  ├─ Branches/
│  ├─ Merge/
│  ├─ Locks/
│  └─ Recovery/
├─ Infrastructure/
│  ├─ LoreSdk/
│  ├─ LoreCli/
│  ├─ Runtime/
│  ├─ Logging/
│  └─ Persistence/
├─ Integration/
│  ├─ Assets/
│  ├─ Scenes/
│  ├─ Prefabs/
│  ├─ ProjectSettings/
│  └─ EditorLifecycle/
└─ UI/
   ├─ Main/
   ├─ Changes/
   ├─ History/
   ├─ Branches/
   ├─ Conflicts/
   ├─ Runtime/
   └─ Diagnostics/

Tests/
└─ Editor/

Documentation~/
```

## 19. Testing / Performance

Testing hierarchy:

``` text
Unit Tests
↓
Application Tests
↓
Lore Backend Integration Tests
↓
Unity Integration Tests
↓
End-to-End Tests
```

検証対象:

-   Create / Modify / Delete / Move / Rename / Meta-only
-   Scene / Prefab / ProjectSettings / Packages
-   External changes
-   Domain Reload
-   Play Mode ON/OFF
-   SDK unavailable / native load failure
-   Remote unavailable / auth failure
-   Push failure after commit
-   Lock race
-   Unity import error
-   GUID/.meta 異常
-   Editor termination during write

Performance:

-   1,000 assets
-   10,000 assets
-   100,000 assets

重点測定:

-   Status refresh
-   Folder aggregation
-   Project Window overlay
-   Large Check In
-   Branch Switch
-   Import storm

## 20. Release CI

``` text
Validate package
↓
Validate package.json / Git tag version
↓
License / Third Party Notices check
↓
Runtime Manifest validation
↓
Official artifact URL validation
↓
Artifact SHA-256 validation
↓
Windows integration tests
↓
macOS integration tests
↓
UPM installation test
↓
GitHub Release
```

Lore Artifact 自体を Lore for Unity の GitHub Release
へ再アップロードしない。

## 21. Implementation Order

  -----------------------------------------------------------------------
  Phase                               内容
  ----------------------------------- -----------------------------------
  1                                   Core types / Result/Error / Backend
                                      interfaces / Runtime Manifest /
                                      Runtime Manager

  2                                   Lore SDK Adapter / CLI Adapter /
                                      Capability Resolver / Repository
                                      detection / Status

  3                                   Application services / Check In /
                                      Push / Sync / Branch / Lock

  4                                   Unity Integration / Asset+.meta /
                                      Scene / Prefab / ProjectSettings

  5                                   StatusStore / Changes UI / Project
                                      overlays / History / Branches

  6                                   Merge / Conflict / Structured Diff
                                      / Recovery

  7                                   Diagnostics / Performance /
                                      Hardening / Release CI
  -----------------------------------------------------------------------

### 21.1 Vertical Slice 1

``` text
UPM install
↓
Install Lore Runtime
↓
Detect Repository
↓
Read Status
↓
Display Changes
↓
Select files
↓
Check In
↓
Push
```

この Slice が通るまで Branch/Merge/Structured Diff
へ実装範囲を広げない。

## 22. Architecture Rules / 禁止事項

-   UI から Lore SDK を直接呼ばない。
-   Unity callback から network access しない。
-   Project Window repaint / OnGUI で Lore query/I/O をしない。
-   Lore 型を Application/UI へ漏らさない。
-   Path/ID をすべて単なる string で扱わない。
-   `.meta` を Asset と無関係に扱わない。
-   Commit成功/Push失敗を Commit失敗扱いしない。
-   Lore operation成功を Unity operation成功と同一視しない。
-   同一 Operation 途中で SDK→CLI fallback しない。
-   Runtime を勝手に download/update/delete しない。
-   System Lore を暗黙利用しない。
-   Recovery Journal なしで大規模 Working Copy write を行わない。
-   Unity Main Thread を Lore I/O 待ちで block しない。
-   UI Toolkit / IMGUI のどちらかを Primary framework と規定しない。

## 23. Specification Freeze

Q16 時点で以下を **FROZEN** とする。

-   Product decisions
-   Lore integration
-   Unity integration
-   Runtime distribution
-   Architecture
-   Implementation boundary

これは変更禁止ではなく、実装都合で暗黙に再解釈しないという意味である。変更時は明示的な仕様変更として扱う。

次フェーズは **Q17: 実装開始**。

最初に `package.json`、asmdef、Core 型、Runtime Manifest、Runtime
Manager、Bootstrap、Backend interfaces から着手する。
