# MyWebApi / MyAppMain

## 概要
MyWebApi、MyAppMain、MyNotificationHub からなる .NET 8 ベースのイベント駆動アプリケーションです。自己ホスト型 Web API を介して IMU 制御コマンドを受け付け、MyAppMain が TCP 経由で IMU と連携し、結果を通知ハブ経由で外部購読者へ配信します。

## 主な機能
- イベントドリブン Web API: `/v1/start` と `/v1/end` を提供し、成功時は 200 OK、同時実行 1 件を超えると 429 を返却
- グローバルレート制限: ASP.NET Core の `PartitionedRateLimiter` で同時処理数を 1 に固定し、キューは未使用
- コントローラ拡張性: `IAppController` 抽象を介して Web API や直接制御など複数コントローラを登録可能
- コマンドパイプライン: バックグラウンドで `ModelCommand` を処理し、相関 ID ごとの `ModelResult` を解決
- IMU TCP クライアント: `ImuClient` が接続、状態確認、データ受信を担い、通知ハブへ同期イベントを発火
- 通知ハブ: `MyNotificationHub` が Start/End、IMU 接続・状態・サンプル、コマンド結果イベントを提供

## リポジトリ構成
```
webapisample/
├── MyWebApi/             # 自己ホスト型 Web API
│   ├── MyWebApiHost.cs
│   └── MyWebApi.csproj
├── MyAppMain/            # アプリケーション本体と IMU 制御
│   ├── MyAppMain.cs
│   ├── CommandPipeline.cs など
│   └── MyAppMain.csproj
├── MyNotificationHub/    # 通知ハブライブラリ
│   ├── MyNotificationHub.cs
│   └── MyNotificationHub.csproj
├── MyAppMain.Tests/      # MSTest ベースのブラックボックステスト
├── docs/                 # 設計ドキュメント (DESIGN.md, IMU_DESIGN.md)
├── scripts/              # 開発支援スクリプト
├── README.md
└── LICENSE
```

## セットアップ
### 必要なツールのインストール

#### ✅ .NET 8 SDK
```bash
# バージョン確認
dotnet --version
# 8.x が表示されることを確認

# インストール（Windows）
winget install Microsoft.DotNet.SDK.8

# インストール（macOS）
brew install dotnet@8

# インストール（Linux）
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0
```

#### ✅ ASP.NET Core ランタイム
```bash
# ランタイム確認
dotnet --list-runtimes
# Microsoft.AspNetCore.App 8.x が表示されることを確認

# インストール（Windows）
winget install Microsoft.DotNet.AspNetCore.8

# インストール（macOS）
brew install --cask dotnet-aspnetcore

# インストール（Linux）
sudo apt-get install -y aspnetcore-runtime-8.0
```

## クイックスタート

### リポジトリのクローンとビルド

```bash
# リポジトリのクローン
git clone <repository-url>
cd webaipsv

# 依存関係の復元
dotnet restore

# 全体ビルド
dotnet build -c Release

# 個別プロジェクトビルド
dotnet build MyWebApi -c Release
dotnet build MyAppMain -c Release
dotnet build MyNotificationHub -c Release
```

## 実行とテスト

```bash
# 全テスト実行
dotnet test -c Release

# 特定プロジェクトのテスト
dotnet test MyAppMain.Tests -c Release

# 詳細ログ付きでテスト実行（Console.WriteLineの出力を表示）
dotnet test MyAppMain.Tests --logger "console;verbosity=detailed"

# テスト一覧表示
dotnet test MyAppMain.Tests --list-tests

# 特定テストの実行
dotnet test MyAppMain.Tests --filter "FullyQualifiedName~MyAppMainBlackBoxTests"
```

### アプリケーションの実行

```bash
# MyAppMainの実行
dotnet run --project MyAppMain

# ホットリロード付き実行
dotnet watch run --project MyAppMain

```

### API 呼び出し例
```bash
curl -X POST http://localhost:5008/v1/start \
  -H "Content-Type: application/json" \
  -d '{"address":"localhost","port":12345}'
```

## API エンドポイント
| メソッド | パス        | 説明                   | 成功レスポンス            | エラー |
|----------|-------------|------------------------|---------------------------|--------|
| POST     | `/v1/start` | IMU 接続の開始要求     | `{"message":"started"}` | レート超過時は 429 |
| POST     | `/v1/end`   | IMU 接続の停止要求     | `{"message":"ended"}`   | レート超過時は 429 |

## アーキテクチャ概要
- Controller: `WebApiController` や `DirectApiController` などが外部入力を `ModelCommand` に変換
- Model: `MyAppMain` がコントローラの起動・停止、IMU 接続、コマンド処理を実行
- View: `MyNotificationHub` の購読者が `ResultPublished` や IMU イベントを受信
- 詳細な設計とデータフローは `docs/DESIGN.md` を参照

## 開発ワークフロー

### Git ワークフロー

#### コミット前の準備
```bash
# コードフォーマット（コミット前に必ず実行）
# 1) CSharpier（自動折返しを含む）
dotnet tool restore
dotnet csharpier format .

# 2) dotnet format（Roserlynベースの補助整形）
dotnet format \
  MyWebApi/MyWebApi.csproj \
  MyAppMain/MyAppMain.csproj \
  MyNotificationHub/MyNotificationHub.csproj \
  MyAppMain.Tests/MyAppMain.Tests.csproj
```

### Gitフックの設定（pre-commit で自動整形）

- 目的: `git commit` 時に CSharpier と dotnet format を自動実行し、差分を自動ステージします。
- セットアップ手順:

```bash
# リポジトリ直下で実行（Git Bash / WSL / macOS / Linux）
bash scripts/install-git-hooks.sh
```

- これで Git の hooks パスが `.githooks` に設定され、`pre-commit` が有効化されます。
- 補足:
  - pre-commit フックは `dotnet tool restore` を自動実行し、ローカルツールが揃っていない環境でも整形が走るようになっています。
  - `dotnet format` は変更のあった C# プロジェクトに対してのみ実行されます。
  - 一時的にフックを無効化してコミットする場合は `git commit --no-verify` を使用してください。

#### コミット規約
```bash
# 機能追加
git commit -m "feat: add new endpoint for data processing"

# バグ修正
git commit -m "fix: handle null reference in JSON parsing"

# ドキュメント更新
git commit -m "docs: update API documentation"

# リファクタリング
git commit -m "refactor: improve error handling in TCP connection"
```

#### ブランチ戦略
```bash
# 機能開発
git checkout -b feature/new-endpoint
git checkout -b fix/tcp-connection-bug

# ブランチ名の例
feature/authentication
fix/rate-limiting-issue
docs/api-documentation
```

## トラブルシューティング

### よくある問題
S
#### .NET ランタイムが見つからない
```bash
# 解決方法
dotnet --list-runtimes
# Microsoft.AspNetCore.App 8.x が表示されない場合
# 上記のランタイムインストール手順を実行
```

#### ポートが既に使用されている
```bash
# 解決方法
# Web API ホストを生成する際に別ポートを指定
// 例: app.RegisterController(new WebApiController(5009));
```

#### ビルドエラー
```bash
# 解決方法
dotnet clean
dotnet restore
dotnet build -c Release
```

#### テストが失敗する
```bash
# 解決方法
dotnet test -c Release --verbosity normal
# 詳細なエラーメッセージを確認
```

## 追加ドキュメント
- `docs/DESIGN.md` — MVC アーキテクチャ、コントローラ抽象化、データフロー
- `docs/IMU_DESIGN.md` — IMU プロトコル、通知 DTO、テスト戦略
- `docs/CONTROLLER_OWNERSHIP.md` — コントローラの所有権モデルと排他制御の詳細
- `AGENTS.md` / `.cursor/rules` — コーディング規約と開発プロセス

## ライセンス
このプロジェクトは [MIT License](LICENSE) の下で提供されています。
