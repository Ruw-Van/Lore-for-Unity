# 二段階 Check In

- Changes の選択は Lore の論理 Asset／`.meta` を組にして扱う。明示的な Stage ボタンで 100 パス以内・保守的なコマンドライン長 12,000 文字以内のバッチを順次 Lore CLI `stage --offline` に渡す。各バッチ後に論理 Asset 組数の進捗を通知し、最後に全件状態を再取得して選択した組だけが Stage 済みか確認する。Lore が暗黙に Stage する選択ファイルの親ディレクトリは許容するが、選択外の Stage 済みファイルは拒否する。ディレクトリそのものを CLI に渡すと子ファイルを一括で Stage してしまうため、Select all では除外し、手動で選んだ場合も実行前に拒否する。
- Stage と Commit は別操作。Check In は全件の Stage 状態、選択外 Stage の不在を再度検証してから１回だけ Commit する。進行状況とキャンセル操作を Lore ウィンドウに表示する。途中の失敗・キャンセルで一部のファイルが Stage 済みになっても自動 Commit／Push／Unstage しない。再試行時は Lore の状態を読み直し、既に Stage 済みの組をスキップして残りを処理する。Push after Check In の初期値はオフ。
- ローカル C# ビルド・スタブ単体テストと、使い捨て Lore Repository で 5,000 組／10,000 ファイルを Stage → 単一 Commit するオフライン CLI 経路を確認した。Unity 実機／Test Runner、Lore 外部サーバ接続は未検証。
