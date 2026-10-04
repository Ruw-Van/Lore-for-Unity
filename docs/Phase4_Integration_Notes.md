# Phase 4 Unity Integration（進行中）

- `Phase3` の HEAD から `Phase4` を分岐。Unity の Project Root が Lore Repository の子ディレクトリでも安全にパスを相互変換する。`Assets/`、`ProjectSettings/`、`Packages/` のパスだけ受け付け、トラバーサルは拒否する。
- Lore Status は `.meta` と Asset 本体を GUID 中心の論理 Asset に集約する。`.meta` のみの変更も保持し、個別の Lore 状態は改変しない。ProjectSettings と Packages は GUID Asset とせず Repository File として保持する。GUID が衝突した投影は公開しない。
- AssetDatabase への GUID 照会は Unity メインスレッドのみ。Bootstrap は Repository 検出後に論理 Asset の初回 Snapshot を生成する。AssetPostprocessor と Scene 保存 callback は変更ヒントを重複排除・debounce し、実際の状態は Lore Status から取り直す。描画 callback から Lore I/O をしない。
- 現段階では Editor 起動時の書き込み Capability を自動有効化しない。Scene/Prefab の未保存編集を勝手に破棄しない Guard は Phase 3 の境界を引き継ぐ。
- Unity Test Runner と macOS arm64 実機は未確認。外部スタブの C# 9.0 ビルド／テストは Unity での検証を代替しない。
