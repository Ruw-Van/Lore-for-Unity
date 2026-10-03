# Phase 3 実装メモ（進行中）

`Phase3` は `Phase2` の HEAD から分岐。Check In / Push / Sync / Branch Switch / Lock の Application Service と CLI v0.10.0 用書き込み Adapter の基礎を追加した。

- Check In は Repository 単位の排他内で全体 Status を走査し、既存の Stage 済み変更や選択外の変更を Commit に混ぜない。Stage 後の再確認に失敗した場合は Commit しない（Stage は自動で取り消さない）。Commit と Push の結果は別々に保持する。
- Sync / Branch Switch は Unity の未保存編集を確認する Guard と永続 Recovery Journal を要求し、Lore 書き込み後に Unity 側の検証が失敗した場合は Journal を完了させない。Guard / Journal の Unity 実装は未完成。
- CLI Adapter は検出済み Repository の Root と絶対パスの検証済み実行ファイルのみを用いる。現状の Editor 起動時構成には書き込み Adapter を接続しておらず、書き込み Capability は公開されない。Unity の Guard / Journal と CLI イベントの実機検証が終わるまで有効化しない。
- CLI 書き込みイベントの `complete`、Commit の Revision／Repository ID を検査するが、Stage／Push／Sync／Branch／Lock の実イベント網羅性と成功後の照合は未検証。失敗やキャンセル後の自動再試行・Backend 切り替えは行わない。
- Unity Test Runner と macOS arm64 実機は未検証。外部 `netstandard2.1` C# 9.0 ビルド／スタブ NUnit テストは Unity 実行の代わりではない。

次は使い捨て Repository で各 CLI 書き込みイベントと State を確認し、Unity main thread の Guard と永続 Journal を実装してから構成に接続する。Phase 4 にはまだ進まない。
