# Phase 3 Application Service 実装メモ

`Phase3` は `Phase2` の HEAD から分岐。Check In / Push / Sync / Branch Switch / Lock の Application Service と CLI v0.10.0 用書き込み Adapter の基礎を追加した。

- Check In は Repository 単位の排他内で全体 Status を走査し、既存の Stage 済み変更を Commit に混ぜない。`Assets/` の選択は本体と `.meta` の変更を一組として Stage し、Stage 後の再確認に失敗した場合は Commit しない（Stage は自動で取り消さない）。Commit 後の Status 再確認が失敗しても Commit 成功を保持し、Push はしない。Commit と Push の結果は別々に保持する。
- Sync / Branch Switch は Unity の未保存編集を確認する Guard と永続 Recovery Journal を要求し、Lore 書き込み後に Unity 側の検証が失敗した場合は Journal を完了させない。`FileRecoveryJournal` は追記専用の境界記録・再起動後の未完了検出・同一 Repository の再実行ブロックを実装済み。保存先にはホストプロジェクトの `Library` 配下を指定し、未完了記録は消さない。Journal の確認・更新時はプロセス間ファイルロックを取得し、競合時は書き込みを拒否する。復旧 UI は未実装。
- `UnityWorkingCopyGuard` は Editor のメインスレッドで未保存 Scene／Prefab Stage／読み込まれた Asset を拒否し、書き込み後に同期 Asset Import を実行する。ただし Unity Test Runner での検証、Domain Reload 後の復旧、遅延コンパイルの完了確認は未実装のため Editor 起動時には接続しない。
- CLI Adapter は検出済み Repository の Root と絶対パスの検証済み実行ファイルのみを用いる。`RuntimeComposition.CreateWrites` は Runtime の `Ready` と空の Recovery Journal、Guard の明示指定が必要。Editor 起動時には呼ばず、書き込み Capability を自動公開しない。SDK read と CLI write の混在も行わない。
- CLI の `complete`、Commit の Revision／Repository ID、Sync の Target、Branch Switch の終了イベントを検査する。Stage は Status 再確認で検証する。Remote を伴う Push／Lock のイベントと成功後の状態照合は未検証。失敗やキャンセル後の自動再試行・Backend 切り替えは行わない。
- Windows x64 の使い捨てローカル Repository で `stage`、`status --scan`、`commit`、`branch switch`、`sync`、`lock acquire` の JSON イベントを確認。相対パスは CLI の作業ディレクトリを Repository Root にする必要がある（Adapter の Runner は対応済み）。`stage` は対象ファイルを Stage しなくても `complete: 0` になり得るため、Check In では必ず Status 再確認を行う。`commit --offline` はローカル Revision 作成後に Relay エラーと二つ目の `complete` が発生しても Exit Code が 0 になり得る。Adapter はローカル Revision イベントを暫定値として読み、Lore の `status --revision-only` で HEAD が一致した場合に限り Commit 成功を返す。実 CLI Adapter での Stage → Commit → 同 Branch への Switch を通し確認済み。`lock acquire --offline` は Remote 不在で失敗した。Remote を伴う Push／Lock は未検証。
- Sync はイベントの Repository ID／Target Revision、Branch Switch は開始・終了イベントと対象 Branch 名を照合する。書き込み後の Unity 同期 import に加え Lore Status を再確認できなければ Journal を完了させない。
- Application Service は Check In／Push／Sync／Branch の一覧・切替／Lock の取得・解放を提供する。Windows x64 の使い捨て Repository で `CheckInService` の Status → Stage → Commit → Status、および CLI Adapter の Stage → Commit → Branch Switch の通し確認に成功。外部 `netstandard2.1`・C# 9.0 ビルドとスタブ NUnit テストでも検証済み。
- Unity Test Runner と macOS arm64 実機は未検証。外部 `netstandard2.1` C# 9.0 ビルド／スタブ NUnit テストは Unity 実行の代わりではない。

Phase 3 の Application Service 実装は完了。Unity Asset／Scene／Prefab の実際の導線と起動時の接続は Phase 4、復旧 UI は Phase 6 の範囲であり、Editor 起動時には書き込みを自動有効化しない。Unity Test Runner、macOS arm64、実 Remote の Push／Lock は検証可能な環境が必要なため未確認として引き継ぐ。これらの実機検証は後工程へ延期する方針とし、Phase 3 を実機検証完了とは扱わない。
