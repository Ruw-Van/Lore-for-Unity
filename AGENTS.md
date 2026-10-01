# 共通の作業方針

- 説明と進捗報告は日本語で書く。内部の検討は英語でもよい。
- コミットメッセージは日本語。1行目に要約、本文には変更の理由と内容だけを書く。AI 関連の記述やブランチ名への言及は入れない（履歴をプロジェクトの変更だけで保ちたいため）。
- worktree で行った作業を develop に取り込むときは Squash マージする。
- push は明示的に頼まれたときだけ行う（共有ブランチへの影響を自分で確認したいため）。

## このリポジトリ

- ルートの `package.json` は Unity Package Manager 用。Unity プロジェクト（`Assets/`、`ProjectSettings/`）や npm スクリプト、CI はこのリポジトリにない。Unity での検証にはパッケージを導入するホストプロジェクトが必要。
- README に列挙された Lore 連携機能はまだ実装済みとは限らない。現状の実装は主に `Editor/Core` の型と `Editor/Infrastructure/Runtime/RuntimeManifest.cs` で、`Application` / `Integration` / `UI` は asmdef のみ。作業前に実コードを確認する。
- `Editor/` の asmdef はすべて Editor 限定で `autoReferenced: false`。依存関係は `Core` ← `Application` ← `Infrastructure` / `Integration` / `UI`（後者 3 つは `Core` も参照）。別アセンブリから使う際は asmdef に明示的な参照を追加する。
- `Tests/Editor` は NUnit の EditMode テスト用 asmdef（`TestAssemblies`、`Core` 参照）。ルートにテスト実行スクリプトはないため、ホスト Unity プロジェクトの Test Runner で実行する。個別確認は EditMode のテスト名で絞る。
- パス型を混同しない: `RepositoryPath` は `\` を `/` に正規化するが、`UnityAssetPath` は `\` を拒否する（`Tests/Editor/Core/PathTests.cs`）。
