# Phase 4 Unity Integration 実装メモ

- `Phase3` の HEAD から `Phase4` を分岐。Unity の Project Root が Lore Repository の子ディレクトリでも安全にパスを相互変換する。`Assets/`、`ProjectSettings/`、`Packages/` のパスだけ受け付け、トラバーサルは拒否する。
- Lore Status は `.meta` と Asset 本体を GUID 中心の論理 Asset に集約する。`.meta` のみの変更も保持し、個別の Lore 状態は改変しない。ProjectSettings と Packages は GUID Asset とせず Repository File として保持する。GUID が衝突した投影は公開しない。
- AssetDatabase への GUID 照会は Unity メインスレッドのみ。Bootstrap は Repository 検出後に論理 Asset の初回 Snapshot を生成する。AssetPostprocessor と Scene 保存 callback は変更ヒントを重複排除・debounce し、実際の状態は Lore Status から取り直す。描画 callback から Lore I/O をしない。
- 現段階では Editor 起動時の書き込み Capability を自動有効化しない。Scene/Prefab の未保存編集を勝手に破棄しない Guard は Phase 3 の境界を引き継ぐ。
- Scene、ProjectSettings、非マージ Asset は Auto Lock 既定 ON。Script とマージ可能なテキストは OFF。Prefab は Unity のテキストシリアライズ時にはマージ可能として扱う。`Project/Lore` の Project Settings からポリシーを保存・変更できる。Undo の編集 intent は callback 内で Lore I/O をせず、Editor update で単一実行の Lock Service に渡す。
- 書き込み接続は `LoreBootstrap.EnableEditingAsync` を明示的に呼ぶ場合だけ有効化し、Recovery Journal の未完了記録があれば拒否する。まだ UI からは呼ばない。
- Unity Project が Lore Repository の内側にある場合は `AssetRootPrefix` を Check In Plan に渡し、`Game/Assets/...` とその `.meta` を一組にする。
- Unity Test Runner と macOS arm64 実機は未確認。外部スタブの C# 9.0 ビルド／テストは Unity での検証を代替しない。
- Sync／Branch Switch 前には未保存 Scene、無名 Scene、Prefab Mode を拒否する。Lore 書き込み後は AssetDatabase を同期更新し、変更されていない Scene 構成だけ再読み込みする。Unity の import／compile が安定するまで Editor update を待ち、途中で状態が変わるか時間切れなら Recovery Journal を未完了に保つ。Unity 実機での Scene 再読込・Domain Reload 挙動の検証は残る。

Phase 4 の Integration 実装は完了。Scene／Prefab／ProjectSettings の EditMode 実機検証は利用可能な Unity ホストがないため後工程へ延期する。書き込み接続は明示的 opt-in のままで、UI からの有効化と Status 表示は Phase 5 に引き継ぐ。Auto Lock の自動検出は Undo 編集 intent で識別できる対象に限り、Undo を通らない ProjectSettings の変更まで検出できたとは扱わない。実 Remote での Lock 確認も残る。
