# Runtime インストール実装メモ

- `Window > Lore > Diagnostics` で明示的に実行するオンライン／ローカルアーカイブの 2 経路。Editor 起動時には検証済みの既存インストールを読み取るだけで、ダウンロードを開始しない。`Window > Lore > Lore` の Setup Required から Diagnostics を開ける。
- オンライン経路は Manifest の公式 GitHub release URL のみを起点とし、HTTPS の公式 Release 資産ホスト以外への redirect を拒否する。資格情報・cookie は使わない。Content-Length があれば事前照合し、ダウンロード途中も固定サイズを超えたら止める。キャッシュ直下の私有 `.download-*` を Install 後に破棄する。
- ローカル経路はユーザーの原本を変更しない。共通の `LoreRuntimeManager.InstallFromFileAsync` で cross-process lock → 私有コピー → サイズ／SHA-256 → archive entries を whitelist・総量上限付きで仮展開 → 実行ファイル hash と `--version` を probe → version/platform のキャッシュパスへ Directory.Move → 最終 probe。異常時は仮展開を破棄し、既存インストールは自動上書きしない。
- Diagnostics の公式ダウンロード中は取得済みバイト数を Manifest の固定サイズで割ったプログレスバーを表示する。取得後は準備・SHA-256 検証・展開／実行ファイル確認・有効化を段階表示する。段階表示は全体の所要時間に対する割合ではない。ローカルアーカイブ経路も準備以降の段階を表示する。
- Windows zip と macOS tar.gz を扱う。tar は通常のフラットなエントリに限定し、リンク、PAX 拡張、階層パスなどは fail closed とする。macOS では公開前に `chmod 0755` を設定する。実際の macOS アーカイブの構成がこの許可リストと異なる場合は安全に拒否する。
- ローカル C# 9.0 ビルド、スタブ HTTP response／redirect／ZIP／tar.gz のテスト、検証済み Windows v0.10.0 ZIP の使い捨てキャッシュへのオフラインインストールと実行ファイル probe のみ確認。Unity 実機・Test Runner と Lore 外部サーバへの接続テスト、公式 URL の実ダウンロード、実 Mac artifact のインストールは未検証。
