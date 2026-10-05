# Phase 5 Status / Editor UI 実装メモ

- `Phase4` の HEAD から `Phase5` を分岐。既存の `StatusStore` の世代付き Snapshot と Phase 4 の GUID／Asset＋`.meta` 投影を Changes タブで読み取る。Repository File は独立して表示し、`.meta` のみの変更も見える。1 ページ 200 件の表示と選択を用意する。
- `Window > Lore > Lore` の Changes／History／Branches タブを追加。Project Window の GUID overlay は公開済みの Snapshot のみを読み、`OnGUI`／repaint から Lore I/O を行わない。短い read に modal progress を出さない。
- History と Branch 一覧は検証済み CLI のオフライン JSON イベントから取得する。`history` はローカル完了後に Relay エラーが続いてもローカル履歴の完了イベントと Repository ID を確認する。使い捨て Repository で 2 件の履歴と 1 件の Branch を確認済み。
- Check In は Commit と Push の結果を別々に表示し、Branch Switch は明示確認の後に実行する。書き込み接続は初回利用時の明示確認を経てからのみ有効化する。未保存 Scene／Prefab と復旧 Journal のガードは維持する。
- Unity 実機／Test Runner と Lore 外部サーバ接続テストは実行しない。外部 C# 9.0 ビルド・スタブテスト・使い捨て Repository のオフライン CLI のみ検証し、実機・外部接続は未検証とする。
