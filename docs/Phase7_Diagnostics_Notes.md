# Phase 7 Diagnostics / Performance / Release 検証メモ

- `Phase6` の HEAD から `Phase7` を分岐。`Window > Lore > Diagnostics` に Runtime、Unity／OS／Architecture、最初に検出した Repository／Branch／Revision、読み書き Backend の状態、Status と Recovery の件数を表示。最初に検出した Branch／Revision は更新後に古くなり得るためその旨を明示する。Copy Diagnostics は allowlist の版・件数・世代・日時のみを出力し、パス、ID、Branch、Revision、CLI 出力と秘密情報を除外する。
- Project overlay の GUID lookup は Snapshot index を使い、Project Window の再描画は投影の参照が変化したときのみ要求する。Lore window は毎フレームの無条件 Repaint を止める。
- ローカルスタブで 1,000／10,000／100,000 Asset の投影・folder 集計・辞書検索を確認。投影時間は実行環境依存で、今回の計測例は 9／21／263 ms。Unity AssetDatabase、Import storm、Scene、CLI Status refresh は測定していない。
- `scripts/validate-package.py` と PR／tag 用 Workflow で UPM metadata、tag version、license／notices、Manifest の公式 URL 形式・SHA-256 形式をオフライン検証。**Runtime の URL 実到達・Artifact の hash 照合や Windows／macOS の Unity／UPM テストと GitHub Release の自動公開は実装していない。** 外部接続・Unity 実機検証を禁止した現在の方針では Release gate を通過した扱いにしない。
- Phase 6 の自動 Resolver chain が未実装のため Phase 7 を含む製品全体は Release Ready ではない。Phase 8 は仕様に範囲がないため未着手。
