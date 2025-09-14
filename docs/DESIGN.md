# 設計: MyWebApi + MyAppMain + IfUtility

## 目標
- 既存の`MyWebApi`を中心としたライブラリ群でPOSTリクエストに対するアプリケーションロジックを実行する
- `MyAppMain`をASP.NET Core/DIに結合させず、代わりに`MyWebApi`のイベントを使用する
- 外部ライブラリは変更せず（新しいインターフェースなし）、`MyAppMain`からのデリゲートで統合する

## コンポーネント
- `MyWebApi` (既存)
  - Kestrelを自己ホストし、バージョン付きエンドポイント`/v1/start`と`/v1/end`を公開
  - 生のPOSTボディ文字列でイベントを発生させる
  - グローバルレート制限を実装（1同時リクエスト、キューなし）
  - 成功時は200 OK、レート制限時は429 Too Many Requestsを返す
- `MyAppMain` (新規)
  - `MyWebApiHost`を`Start(int port)`/`Stop()`でラップするオーケストレーター
  - `MyWebApi`イベントに購読し、TCP接続ロジックとIfUtilityメソッドを呼び出す
  - 直接TCP接続を管理し、JSONペイロードからサーバー情報を抽出する
- `IfUtility` (既存)
  - 現在は空の実装（将来の拡張用）
  - テスト用のサブクラス化をサポートする仮想メソッドを提供

## パブリック契約
MyWebApiの型との結合を避けるため、イベントとデリゲートは生のJSON文字列を使用する。

```csharp
// MyWebApiHostが公開するイベント（シンプルさのため同期）
public event Action<string>? StartRequested; // 生のボディ
public event Action<string>? EndRequested;   // 生のボディ
```

注意事項:
- イベントは同期処理のために`Action<string>`を使用する
- ハンドラー内での非同期操作が必要な場合は、Task.Runやasync voidパターンを検討する
- イベントハンドラーは非同期で並行実行される（Task.Runでラップ）

## データフロー
1. クライアントがJSONボディ`{ "address": "localhost", "port": 8080 }`で`POST /v1/start`または`/v1/end`を送信
2. Minimal APIがボディを文字列として読み取る
3. `MyWebApiHost`が生のJSON文字列で`StartRequested`/`EndRequested`を発生させる
4. `MyAppMain`の購読済みハンドラーが呼び出される：
   - まず`IfUtility.HandleStart`/`HandleEnd`を呼び出し
   - JSONからアドレスとポートを抽出してTCP接続を確立/切断

## MyAppMainの責任
- ライフサイクル管理:
  - `Start(int port)`: イベントに購読し、指定されたポートで`MyWebApiHost`を開始
  - `Stop()`: `MyWebApiHost`を停止し、ハンドラーの購読を解除
- 統合ポイント:
  - コンストラクターで`IfUtility`インスタンスを受け取る（デフォルトで新規作成）
  - イベントハンドラー内でTCP接続ロジックを直接実装
  - JSONペイロードからサーバー情報を抽出してTCP接続を管理

使用例:

```csharp
// デフォルトのIfUtilityを使用
var app = new MyAppMain();
app.Start(5008);

// またはカスタムのIfUtilityを注入
var customUtility = new TestIfUtility();
var app2 = new MyAppMain(customUtility);
app2.Start(5008);

// 停止
app.Stop();
```

## 依存関係の境界
- `MyAppMain`はASP.NET Coreの抽象化を参照しない（`IServiceCollection`、`WebApplication`などなし）
- 調整はイベントとプレーンDTOのみで行われる
- `IfUtility`は変更されない；`MyAppMain`に注入されたインスタンスを通じて呼び出される
- TCP接続ロジックは`MyAppMain`内に直接実装されている

## バージョニング
- エンドポイントは`/v1`でグループ化され、`/v2`での将来の破壊的変更を可能にしつつ`/v1`を安定に保つ

## テスト戦略（ブラックボックス）
- テストプロジェクト`MyAppMain.Tests`はMSTestを使用
- `MyAppMain`を空いているポートで開始
- 呼び出されたときに`TaskCompletionSource<string>`を完了するフェイクのIfUtilityを注入
- `HttpClient`を使用して`/v1/start`と`/v1/end`にPOSTし、フェイクが呼び出され期待されるJSONを含むことをアサート
- `TcpListener`をポート0にバインドして空いているポートを割り当てるヘルパーを使用

テスト例:

```csharp
[TestMethod]
public async Task Posting_Start_Triggers_External_Handler()
{
    var startTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var testUtility = new TestIfUtility { OnStart = json => startTcs.SetResult(json) };
    var app = new MyAppMain(testUtility);
    var port = GetFreePort();
    try
    {
        app.Start(port);
        var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        var json = "{\"address\":\"localhost\",\"port\":8080}";
        var res = await client.PostAsync("/v1/start", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        var received = await startTcs.Task.TimeoutAfter(TimeSpan.FromSeconds(3));
        StringAssert.Contains(received, "\"localhost\"");
    }
    finally { app.Stop(); }
}
```

## 並行性とエラーハンドリング
- レート制限: グローバルレート制限により1同時リクエストのみ許可（キューなし）。制限超過時は429 Too Many Requestsを返す
- 複数購読者: 複数のハンドラーがアタッチされている場合、すべて非同期で並行実行される
- ハンドラー障害: ポリシーを決定する（フェイルファスト vs. ログして継続）。デフォルト推奨: キャッチ、ログ、失敗が表面化する必要がない限りクライアントに200 OKを返す
- TCP接続エラー: 接続失敗時はログに記録し、アプリケーションは継続実行される
- タイムアウト: ビジネス要件に応じてハンドラー内にタイムアウトロジックを実装することを検討

## セキュリティ考慮事項
- サンプルエンドポイントは認証なし；信頼されたネットワークを超えて公開する場合は認証（APIキー、JWT）を追加
- 本番環境ではHTTPSを推奨；`app.Urls`を適切に設定
- ペイロードサイズを検証/制限して悪用を防止

## 未解決の課題 / 将来の作業
- start/endはべき等であるべきか、相関IDを持つべきか？
- ハンドラーが長時間実行される場合のバックプレッシャー/キューイング
- 構造化ログと診断イベント
- TCP接続の永続化と再接続ロジック
- IfUtilityの具体的な実装とビジネスロジックの追加
