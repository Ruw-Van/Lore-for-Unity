# Phase 8: Phase 6／7 の残件

ユーザー指定により `Phase7` の HEAD から `Phase8` を分岐し、実機・外部接続テストなしで安全に確認できる部分を補強した。

- 競合ファイルの候補順（Unity YAML → 設定済み外部ツール → テキスト → バイナリの版選択）を純粋な選択モデルとして実装。外部ツールは未設定時には候補に含めない。Changes UI の競合表示は候補の**案内だけ**で、プレビューや候補発見を Lore native conflict の解消として報告しない。
- 既存の適用済み Merge Recovery Journal を確認した上で、Lore Status が競合と報告する Asset と競合 `.meta` の両方に対して、確認ダイアログ後に native `branch merge resolve mine/theirs` を実行する経路を追加。Repository gate、Unity 事前・事後検査、CLI イベント検証、Lore Status の再確認を通し、Recovery は自動完了させない。CLI 版選択の実操作はスタブでのみ検証済みで、Unity 実機では未検証。
- `scripts/validate-package.py --artifact Windows-x64 <既存のローカル zip>` でダウンロードなしにサイズ・SHA-256 を検証。既存の pinned Windows archive は照合成功、誤ったファイルは拒否した。メタデータ検査は Linux／Windows／macOS の GitHub Actions matrix に拡張。
- Diagnostics の Plugin version は固定文字列ではなく Unity Package Manager の導入パッケージから取得し、取得不能なら `unavailable` にする。

## 引き続き未完了

- Unity 構造化編集／外部 merge tool 起動／テキスト自動解決は未実装。Binary choose-version は明示的な破棄確認が必要で、自動選択はしない。CLI 版選択が失敗／中断した場合は Recovery を保持して Lore Status の再確認を要求する。
- Artifact の URL 到達性と macOS の実ファイル照合は未検証。Unity Editor／Test Runner、Windows／macOS の UPM 実装・統合テスト、Lore 外部サーバ接続、Release 発行は実施していない。自動 Release は構成せず、現時点で Release Ready とはしない。
