# 新規開発者のオンボーディングガイド

## 概要
このガイドは、新しい開発者が MyWebApi + MyAppMain プロジェクトに迅速に参加できるよう設計されています。.NET 8 ベースのイベント駆動アーキテクチャを持つ自己完結型Web APIプロジェクトです。

---

## 1. 環境セットアップ

### 必要なツールのインストール

#### ✅ .NET 8 SDK
```bash
# バージョン確認
dotnet --version
# 8.x が表示されることを確認

# インストール（Windows）
winget install Microsoft.DotNet.SDK.8

# インストール（macOS）
brew install --cask dotnet-sdk

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

#### ✅ 推奨IDE・エディタ
- **Visual Studio 2022** (Windows) - Community版で十分
- **Visual Studio Code** + C# Dev Kit 拡張機能
- **JetBrains Rider** (クロスプラットフォーム)

#### ✅ Git設定
```bash
# Git設定
git config --global user.name "Your Name"
git config --global user.email "your.email@example.com"

# SSH鍵生成（GitHub使用の場合）
ssh-keygen -t ed25519 -C "your.email@example.com"
# 公開鍵をGitHubに追加
```

---

## 2. プロジェクトの把握

### プロジェクト構成の理解

#### 📁 ディレクトリ構造
```
webaipsv/
├── MyWebApi/              # Web API ホスト
│   ├── MyWebApiHost.cs    # メインクラス
│   └── MyWebApi.csproj    # プロジェクトファイル
├── MyAppMain/             # オーケストレーター
│   ├── MyAppMain.cs       # メインロジック
│   └── MyAppMain.csproj   # プロジェクトファイル
├── MyAppNotificationHub/  # 外部通知用イベントハブ
│   ├── MyAppNotificationHub.cs   # ハブクラス
│   └── MyAppNotificationHub.csproj # プロジェクトファイル
├── MyAppMain.Tests/       # テストプロジェクト
│   ├── MyAppMainBlackBoxTests.cs
│   └── MyAppMain.Tests.csproj
├── docs/                  # ドキュメント
│   └── DESIGN.md          # アーキテクチャ設計
├── .cursor/               # Cursor設定
│   └── rules              # コーディング規約
├── README.md              # プロジェクト概要
└── AGENT.md               # 開発ガイドライン
```

#### 🏗️ アーキテクチャ概要
- **MVC構成**: Controller（WebAPI ほか拡張可）/ Model（MyAppMain）/ View（MyAppNotificationHub購読者）
- **複数コントローラ対応**: 将来的にCLIやMQ等のコントローラ追加が可能
- **処理結果通知**: モデル処理完了後にのみ`MyAppNotificationHub`経由でビューへ通知
- **コンテキスト分離**: コントローラ処理とビュー通知は別スレッドで分離
- **レート制限**: 1同時接続制限でDDoS攻撃を防止
- **バージョニング**: `/v1` ルートグループでAPIバージョン管理
- **起動/停止API**: `MyWebApiHost` は `StartAsync/StopAsync` のみを公開（同期APIは `MyAppMain.Start/Stop` を利用）

### 主要ドキュメントの読書

#### 📚 必須読書リスト
1. **README.md** - プロジェクト概要とクイックスタート
2. **docs/DESIGN.md** - アーキテクチャと設計思想
3. **AGENT.md** - コーディング規約と開発ワークフロー
4. **.cursor/rules** - プロジェクト固有のルール

---

## 3. 開発環境のセットアップ

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
dotnet build MyAppNotificationHub -c Release
```

### テストの実行

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

# 特定ポートはコードで指定（MyAppMain.Start）
# 例: var app = new MyAppMain.MyAppMain(); app.Start(5008);
```

---

## 4. 初回動作確認

### エンドポイントのテスト

```bash
# アプリケーション起動後、別ターミナルで実行

# Start エンドポイントのテスト
curl -X POST http://localhost:5008/v1/start \
  -H "Content-Type: application/json" \
  -d '{"message":"hello","address":"127.0.0.1","port":8080}'

# End エンドポイントのテスト
curl -X POST http://localhost:5008/v1/end \
  -H "Content-Type: application/json" \
  -d '{"message":"bye"}'

# レート制限のテスト（同時に複数リクエスト）
curl -X POST http://localhost:5008/v1/start \
  -H "Content-Type: application/json" \
  -d '{"message":"test1"}' &
curl -X POST http://localhost:5008/v1/start \
  -H "Content-Type: application/json" \
  -d '{"message":"test2"}'
```

### 期待される動作
- **成功時**: `200 OK` レスポンス
- **レート制限時**: `429 Too Many Requests` レスポンス
- **TCP接続**: JSONに`address`と`port`が含まれる場合、TCPサーバーに接続

---

## 5. 開発ワークフロー

### コーディング規約

#### C# スタイル
- **インデント**: 4スペース
- **名前空間**: ファイルスコープ名前空間
- **命名規則**: 
  - PascalCase: 型、メソッド、プロパティ、イベント
  - camelCase: ローカル変数、パラメータ
  - インターフェース: `I` プレフィックス

#### プロジェクト構造
- 1ファイルに1つのトップレベル型
- 機能別にファイルをグループ化
- コントローラーは `Controllers/` ディレクトリ
- 共有サービスは `Services/` ディレクトリ

### Git ワークフロー

#### コミット前の準備
```bash
# コードフォーマット（コミット前に必ず実行）
dotnet format \
  MyWebApi/MyWebApi.csproj \
  MyAppMain/MyAppMain.csproj \
  MyAppNotificationHub/MyAppNotificationHub.csproj \
  MyAppMain.Tests/MyAppMain.Tests.csproj
```

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

---

## オンボーディングチェックリスト

### ✅ 環境セットアップ
- [ ] .NET 8 SDK がインストール済み (`dotnet --version` で確認)
- [ ] ASP.NET Core ランタイムがインストール済み (`dotnet --list-runtimes` で確認)
- [ ] 推奨IDE・エディタがセットアップ済み
- [ ] Git設定が完了済み
- [ ] SSH鍵が設定済み（GitHub使用の場合）

### ✅ プロジェクト理解
- [ ] プロジェクト構成を理解済み
- [ ] アーキテクチャの基本概念を把握済み
- [ ] 主要ドキュメントを読了済み
- [ ] イベント駆動設計の仕組みを理解済み

### ✅ 開発環境
- [ ] リポジトリをクローン済み
- [ ] 依存関係の復元が完了済み
- [ ] 全プロジェクトのビルドが成功
- [ ] 全テストがパスしている
- [ ] ローカルでアプリケーションを実行できる

### ✅ 動作確認
- [ ] エンドポイントのテストが成功
- [ ] レート制限の動作を確認済み
- [ ] TCP接続機能をテスト済み
- [ ] エラーハンドリングを確認済み

### ✅ 開発準備
- [ ] コーディング規約を理解済み
- [ ] Git ワークフローを把握済み
- [ ] コミット規約を理解済み
- [ ] ブランチ戦略を理解済み

### ✅ 初回貢献
- [ ] 最初のブランチを作成済み
- [ ] 小さな変更を実装済み（例：コメント追加、ログ改善）
- [ ] テストを追加・更新済み
- [ ] プルリクエストを作成済み

---

## トラブルシューティング

### よくある問題

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
# コード内で MyAppMain の Start に別ポートを指定
// 例: app.Start(5009); // 5008の代わりに5009を使用
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

---

## 次のステップ

### 推奨学習リソース
1. **ASP.NET Core 公式ドキュメント**
2. **.NET 8 新機能ガイド**
3. **イベント駆動アーキテクチャのベストプラクティス**
4. **セキュリティベストプラクティス**

### 貢献の機会
1. **新機能の追加**: 認証・認可機能
2. **テストの拡充**: エッジケースのテスト
3. **ドキュメントの改善**: API仕様書の作成
4. **セキュリティの強化**: HTTPS対応、入力検証

---

## サポート

### 質問・サポート
- **技術的な質問**: プロジェクトのIssuesで質問
- **設計に関する質問**: `docs/DESIGN.md` を参照
- **コーディング規約**: `.cursor/rules` を参照

### 定期的な確認事項
- 依存関係の更新
- セキュリティパッチの適用
- テストカバレッジの確認
- パフォーマンスの監視

**オンボーディング完了おめでとうございます！** 🎉

このガイドに従って、プロジェクトに貢献する準備が整いました。何か質問があれば、遠慮なくチームに相談してください。
