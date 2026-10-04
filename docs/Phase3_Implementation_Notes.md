# Phase 3 実装メモ（進行中）

`Phase3` は `Phase2` の HEAD から分岐。Check In / Push / Sync / Branch Switch / Lock の Application Service と CLI v0.10.0 用書き込み Adapter の基礎を追加した。

- Check In は Repository 単位の排他内で全体 Status を走査し、既存の Stage 済み変更や選択外の変更を Commit に混ぜない。Stage 後の再確認に失敗した場合は Commit しない（Stage は自動で取り消さない）。Commit と Push の結果は別々に保持する。
- Sync / Branch Switch は Unity の未保存編集を確認する Guard と永続 Recovery Journal を要求し、Lore 書き込み後に Unity 側の検証が失敗した場合は Journal を完了させない。`FileRecoveryJournal` は追記専用の境界記録・再起動後の未完了検出・同一 Repository の再実行ブロックを実装済み。保存先にはホストプロジェクトの `Library` 配下を指定し、未完了記録は消さない。複数 Editor プロセス間の排他と復旧 UI は未実装。
- `UnityWorkingCopyGuard` は Editor のメインスレッドで未保存 Scene／Prefab Stage／読み込まれた Asset を拒否し、書き込み後に同期 Asset Import を実行する。ただし Unity Test Runner での検証、Domain Reload 後の復旧、遅延コンパイルの完了確認は未実装のため Editor 起動時には接続しない。
- CLI Adapter は検出済み Repository の Root と絶対パスの検証済み実行ファイルのみを用いる。現状の Editor 起動時構成には書き込み Adapter を接続しておらず、書き込み Capability は公開されない。Unity の Guard / Journal と CLI イベントの実機検証が終わるまで有効化しない。
- CLI 書き込みイベントの `complete`、Commit の Revision／Repository ID を検査するが、Stage／Push／Sync／Branch／Lock の実イベント網羅性と成功後の照合は未検証。失敗やキャンセル後の自動再試行・Backend 切り替えは行わない。
- Windows x64 の使い捨てローカル Repository で `stage`、`status --scan`、`commit`、`branch switch`、`sync`、`lock acquire` の JSON イベントを確認。相対パスは CLI の作業ディレクトリを Repository Root にする必要がある（Adapter の Runner は対応済み）。`stage` は対象ファイルを Stage しなくても `complete: 0` になり得るため、Check In では必ず Status 再確認を行う。`commit --offline` はローカル Revision 作成後に Relay エラーと二つ目の `complete` が発生し得る。この場合は成功扱いせず、実際の Revision を Status で再照会する。`lock acquire --offline` は Remote 不在で失敗した。Remote を伴う Push／Lock は未検証。
- Unity Test Runner と macOS arm64 実機は未検証。外部 `netstandard2.1` C# 9.0 ビルド／スタブ NUnit テストは Unity 実行の代わりではない。

次は復旧 UI、書き込み後の状態照合と Domain Reload 後の検証を実装し、Unity Test Runner と実 Remote で検証してから構成に接続する。Phase 4 にはまだ進まない。
