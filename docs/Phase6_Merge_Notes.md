# Phase 6 Merge / Diff / Recovery メモ

- `Phase5` の HEAD から `Phase6` を分岐。Merge は選択した Backend を固定し、Sync／Branch Switch と同じ Repository gate、Unity 事前・事後検査、Recovery Journal、Status 再取得を通す。CLI の `branch merge` JSON に `hasConflicts: 1` がある場合、CLI の `complete: 0` を成功扱いせず、Lore native conflict として表示する。
- Conflict の状態は Lore Status にのみ依存する。Changes の競合表示、Recovery pending の表示と明示的な確認を追加。Journal を完了する前に Status を再取得し、競合が残っていれば拒否する。Journal は削除せず完了マーカーを記録する。ユーザーが Lore 側で競合を解決または Merge を中止し、Working Copy を確認してから復旧を承認する。
- Simple Diff が標準。CLI JSON の単一ファイル patch を検証して表示する。Structured は Unity YAML の object anchor を含む hunk に限って変更行を object 別にグループ化し、帰属不明の hunk／非対応拡張子は拒否して Simple Diff を案内する。これは自動マージ・構文木 Diff ではない。
- **未実装:** Unity Structured Resolver／外部 merge tool／text resolver／binary choose-version resolver の自動 chain。競合ファイルの内容を推測して上書きするより、安全に Lore native の手動解決を要求する。現在はこの制約があるため Phase 6 の仕様全体は未完了。
- Unity 実機／Test Runner と Lore 外部サーバ接続テストは実施しない。C# 9.0 ビルド、スタブテスト、使い捨て Repository のオフライン CLI のみ確認し、Unity 実機・外部接続は未検証。
