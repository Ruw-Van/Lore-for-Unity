# Unity プロジェクトの Lore 初期化

- Lore ウィンドウの初期化ボタンは、検証済み Runtime が利用でき、現在の Unity プロジェクトとその親階層に `.lore` がなく、まだ repository 検出中・初期化中でない場合にだけ表示する。`Assets` フォルダではなく Unity プロジェクトのルートを対象とする。
- 押下時に対象パスと「オフラインのローカル作業コピー、既存ファイルの stage/push なし」を確認する。実行直前にも Unity プロジェクト構造と親を含む `.lore` の不在を再確認し、検証済み Lore v0.10.0 CLI に `--offline repository create --vfs none` を渡す。
- 作成後は Lore CLI の status から ID とルートを検証し、Changes の初回取得を実行する。Lore が作成した `.lore` は、後続の検証失敗やキャンセル時でも自動削除しない。既存の `.lore` が壊れている場合は初期化候補にしない。
- ローカルのスタブ単体テストと使い捨てディレクトリへのオフライン CLI 初期化・status のみ確認した。Unity 実機／Test Runner と Lore 外部サーバ接続は実行していない。
