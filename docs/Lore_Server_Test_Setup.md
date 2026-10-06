# Lore Server v0.10.0 の起動と隔離環境での試用

この文書は **Lore v0.10.0** の公式資料を基にした手順書であり、このリポジトリからサーバを起動した記録ではない。`loreserver` は **Lore CLI / Unity Package に含まれる `lore` とは別の実行ファイル**。以下のデモ構成は認証なし・一時保存・自己署名証明書で、公開ネットワークや重要データには使わない。

**既存作業コピーについて:** Lore for Unity の「Initialize this Unity project with Lore」は `--offline repository create` を実行する。これで作った `.lore/config.toml` には remote URL がない。サーバを立てても自動接続されず、既存の `remote_url` を手編集して移行することも Lore 公式資料は推奨していない。接続試験は**別の使い捨て Unity プロジェクト／作業コピー**を作り、サーバ起動後に公式 CLI で `lore repository create lore://127.0.0.1:41337/<repo-name>` するか clone する。Unity Package は PATH 上の CLI を暗黙利用しないが、手動での remote 作業コピー作成には公式 CLI を別途用意する。現在のプラグインに既存の offline Repository を remote 化する UI はない。

## 通信・共通確認

- `41337/udp` = QUIC（Lore クライアント向け）、`41337/tcp` = gRPC、`41339/tcp` = HTTP（`/health_check`）。**HTTP だけが通っても QUIC の接続は保証されない。** 同じ番号の TCP と UDP は別々に公開する。
- デモ用サーバは前景プロセスで起動し、`Ctrl+C` で止める。別の端末から `curl.exe -i http://127.0.0.1:41339/health_check`（Linux では `curl -i ...`）を実行し、`HTTP/1.1 200 OK` を確認する。このコマンドは**利用者が試すときの手順**であり、本リポジトリでの実行結果ではない。
- 初期設定は一時ディレクトリ内のストアと再起動ごとに変わる `localhost` 用自己署名 QUIC 証明書を使い、認証は無効。ホスト外へポートを公開しない。VM の IP や LAN 名で接続するなら、そのアドレスに合う SAN を持つ証明書と信頼設定、ファイアウォールを別途準備する。
- 起動時の作業ディレクトリに `lore-server/config` があると設定が読み込まれ得る。既存の Lore ソースツリーで起動する場合は設定内容を確認するか、空の別ディレクトリから起動する。

## Windows x64: 配布バイナリで試す

PowerShell で公式 [v0.10.0 リリース](https://github.com/EpicGames/lore/releases/tag/v0.10.0)の Windows x64 **loreserver** アーカイブを取得する。`lore.exe` の ZIP と取り違えないこと。スクリプトを無検証でパイプ実行せず、展開内容と公開元を確認する。

```powershell
$test = Join-Path $env:LOCALAPPDATA 'LoreForUnity\ServerTest'
New-Item -ItemType Directory -Force $test | Out-Null
Invoke-WebRequest 'https://github.com/EpicGames/lore/releases/download/v0.10.0/loreserver-v0.10.0-x86_64-pc-windows-msvc.zip' -OutFile (Join-Path $test 'loreserver.zip')
Expand-Archive -LiteralPath (Join-Path $test 'loreserver.zip') -DestinationPath (Join-Path $test 'bin')
$server = Get-ChildItem (Join-Path $test 'bin') -Filter loreserver.exe -Recurse -File | Select-Object -First 1 -ExpandProperty FullName
if (-not $server) { throw 'loreserver.exe がアーカイブ内にありません' }
& $server --version
& $server
```

別 PowerShell から `curl.exe -i http://127.0.0.1:41339/health_check`。v0.10.0 以外を試すときはサーバと CLI の版を揃える。デモ設定は再起動後のストア・証明書を保証しない。

## Docker Desktop / WSL2 統合（Windows 上の手軽な隔離案）

Docker Desktop を WSL2 backend で使用し、**v0.10.0 タグ**の Lore ソースを取得する。Docker イメージは公式手順どおり `lore-server/Dockerfile` からビルドする（数 GB の空き RAM が必要）。ホスト側はループバックにだけ bind する。Apple Silicon / Windows の Docker build は公式推奨どおり `linux/amd64` を指定する。

```powershell
git clone --branch v0.10.0 --depth 1 https://github.com/EpicGames/lore.git lore-server-v0.10.0
cd lore-server-v0.10.0
docker build --platform linux/amd64 -f lore-server/Dockerfile -t lore-server:test-v0.10.0 .
docker run --rm --name lore-server-test `
  -p 127.0.0.1:41337:41337/tcp `
  -p 127.0.0.1:41337:41337/udp `
  -p 127.0.0.1:41339:41339/tcp `
  lore-server:test-v0.10.0
```

この例は使い捨て。停止は別端末の `docker stop lore-server-test`。Dockerfile はコンテナ内 `/data` を使うため、削除後も残す必要がある場合は公式の volume・証明書・`local.toml` 併用手順に切り替える。Docker Desktop の Windows→コンテナ UDP/TCP 公開は環境・FW の影響を受けるため、HTTP health と実際の QUIC 接続は別々に確認する。

### WSL2 内で Linux バイナリを直接起動したい場合

Windows x64 ホストの x86_64 Linux ディストリビューションを想定する。Ubuntu 等を WSL2 に導入したら、**WSL 内の**端末で公式リリースの `x86_64-unknown-linux-gnu` アーカイブを使う。`aarch64-unknown-linux-gnu-neoverse-512tvb` は Graviton3/SVE 向けで通常の arm64 PC 用ではない。

```bash
mkdir -p ~/lore-server-test/bin
curl -fL https://github.com/EpicGames/lore/releases/download/v0.10.0/loreserver-v0.10.0-x86_64-unknown-linux-gnu.tar.gz -o ~/lore-server-test/loreserver.tar.gz
tar -tzf ~/lore-server-test/loreserver.tar.gz  # 内容を確認
tar -xzf ~/lore-server-test/loreserver.tar.gz -C ~/lore-server-test/bin
server=$(find ~/lore-server-test/bin -type f -name loreserver -print -quit)
test -n "$server" || { echo 'loreserver がアーカイブ内にありません' >&2; exit 1; }
"$server" --version
"$server"
```

WSL 内で `curl -i http://127.0.0.1:41339/health_check`。Windows 側からの接続は [Microsoft の WSL ネットワーク資料](https://learn.microsoft.com/windows/wsl/networking) を参照する。既定 NAT の `localhost` 転送で HTTP は確認できても QUIC/UDP が通るとは限らない。Windows 11 22H2 以降の mirrored mode は Windows ↔ WSL の `localhost` 接続をサポートするが、UDP・ファイアウォール・証明書を含む Lore の実通信は**未検証**。WSL IP に直接接続するなら `wsl.exe hostname -I`（大文字 `I`）で確認し、動的 IP・UDP の経路・証明書の SAN を考慮する。TCP 専用の `netsh interface portproxy` だけでは Lore の QUIC/UDP を転送できない。

## 専用 Linux VM（例: VirtualBox NAT）

VM に x86_64 Linux を入れ、上の Linux アーカイブを **VM 内**で展開・起動する。VirtualBox のアダプタ 1 を NAT にし、VM を停止した状態で次のポート転送を登録する。`LoreTestVM` は実際の VM 名に置き換える。ホストを `127.0.0.1` に固定して LAN には公開しない。

```powershell
VBoxManage modifyvm 'LoreTestVM' --natpf1 'lore-quic,udp,127.0.0.1,41337,,41337'
VBoxManage modifyvm 'LoreTestVM' --natpf1 'lore-grpc,tcp,127.0.0.1,41337,,41337'
VBoxManage modifyvm 'LoreTestVM' --natpf1 'lore-http,tcp,127.0.0.1,41339,,41339'
```

VM 起動後は **ゲスト内** `curl -i http://127.0.0.1:41339/health_check`、次に **ホスト側** `curl.exe -i http://127.0.0.1:41339/health_check` を確認する。ホスト→ゲストの QUIC は別途実際の Lore クライアントで確認する。ポートが使用中なら重複サービスを止め、別のホストポートへ変える場合は Lore URL も合わせる。VM のホストオンリー接続も選べるが、ゲスト IP で接続するなら証明書の SAN とゲスト FW を設定する。VirtualBox 以外の VM は対応する NAT/TCP・UDP 転送機能に読み替える。

## 一時環境ではなく保存したい場合

公式ガイドの [Run from the binary / Make it persistent](https://github.com/EpicGames/lore/blob/v0.10.0/docs/how-to/deploy-local-lore-server.md#run-from-the-binary) を使用する。`--config <DIR>` で `local.toml` を読み込み、`[immutable_store.local]` と `[mutable_store.local]` の `path` を永続ディスクに指定し、`[server.quic.certificate]` の `cert_file` / `pkey_file` を固定する。例（Linux、絶対パスは環境に合わせて変更）:

```toml
# /opt/loreserver/config/local.toml
[server.quic.certificate]
cert_file = "/opt/loreserver/certs/cert.pem"
pkey_file = "/opt/loreserver/certs/key.pem"

[immutable_store.local]
path = "/opt/loreserver/store"
flush_delay_seconds = 10

[mutable_store.local]
path = "/opt/loreserver/store"
flush_delay_seconds = 10
```

証明書は公開先の名前／IP に一致する SAN を指定して発行・保護する。`loreserver --config /opt/loreserver/config` で起動し、保存先をバックアップする。**この設定だけで本番向けの認証・ネットワーク制限が完成するわけではない。** 既定の public endpoint は `0.0.0.0`、認証は無効なので、ホスト FW／ネットワーク境界で制限してから利用する。

## 情報源と検証範囲

- [Lore v0.10.0: Deploy a local Lore Server](https://github.com/EpicGames/lore/blob/v0.10.0/docs/how-to/deploy-local-lore-server.md)
- [Lore v0.10.0: Quickstart](https://github.com/EpicGames/lore/blob/v0.10.0/docs/tutorials/quickstart.md)
- [Lore v0.10.0: Server config reference](https://github.com/EpicGames/lore/blob/v0.10.0/docs/reference/lore-server-config.md)
- [Lore v0.10.0: CLI config reference](https://github.com/EpicGames/lore/blob/v0.10.0/docs/reference/lore-cli-config.md)（offline Repository の remote 制約）
- [Microsoft: Accessing network applications with WSL](https://learn.microsoft.com/windows/wsl/networking)
- [Oracle VirtualBox: Virtual Networking / NAT port forwarding](https://www.virtualbox.org/manual/ch06.html#natforward)

上記のコマンドと構成は文書・公開リリース資産から確認した例であり、**サーバ実起動、Unity 実機／Test Runner、Lore 外部サーバ接続、WSL2・Docker・VM 上の疎通試験は行っていない**。
