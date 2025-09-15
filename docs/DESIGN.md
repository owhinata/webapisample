# 設計: MVC（Controller/Model/View）

## 目標
- MVCに整理し、複数コントローラを登録可能にする
- モデル処理完了後にのみビューへ結果通知を行う
- コントローラ処理コンテキストとビュー通知コンテキストを分離する

## コンポーネント（MVC）
- Controller（複数可）
  - 例: `MyWebApiHost`（Web API）をアダプタで`IAppController`化
  - 外部入力を`ModelCommand`としてモデルに渡す
  - レート制限、入力検証、相関IDの生成などはコントローラ側で実施可
- Model（MyAppMain）
  - 受け取ったコマンドを処理し`ModelResult`を生成
  - TCP接続/切断などアプリの中核ロジックを担う
  - 複数コントローラを登録/開始/停止できる
- View（AppEventJunction購読者）
  - モデル処理の「結果」を受け取り、UI等に反映
  - 通知は別スレッドから同期イベントで発火（非同期化は購読側に委譲）

## コントローラ抽象化（IAppController）
```csharp
public interface IAppController
{
    string Id { get; }
    event Action<ModelCommand>? CommandRequested; // 同期発火
    Task<bool> StartAsync(CancellationToken ct = default);
    Task<bool> StopAsync(CancellationToken ct = default);
}
```
既存の`MyWebApiHost`は、薄いアダプタ（例: `WebApiControllerAdapter`）で`IAppController`に適合させ、
`/v1/start`や`/v1/end`のPOST受信時に`CommandRequested`を発火する。

## データ契約
```csharp
public record ModelCommand(
    string ControllerId,
    string Type,              // "start" | "end" | ...
    string RawJson,
    string? CorrelationId,
    DateTimeOffset Timestamp);

public record ModelResult(
    string ControllerId,
    string Type,
    bool Success,
    string? Error,
    object? Payload,
    string? CorrelationId,
    DateTimeOffset CompletedAt);
```

`AppEventJunction`は「結果通知」のイベントを提供する。
```csharp
public sealed class AppEventJunction
{
    public event Action<ModelResult>? StartCompleted; // 同期イベント
    public event Action<ModelResult>? EndCompleted;   // 同期イベント
}
```

## データフロー
1. コントローラが外部入力を受け取り`ModelCommand`を発火
2. `MyAppMain`はコマンドを受理して非同期ワーカーで処理
3. 処理完了後に`ModelResult`を生成
4. 専用の通知ディスパッチャが`AppEventJunction`のイベントを同期発火（UIなどが購読）

## MyAppMainの責任
- ライフサイクル管理:
  - `Start(int port)`/`Stop()`: 互換APIを維持しつつ、登録済みコントローラを起動/停止
- コマンド処理:
  - `Channel<ModelCommand>`で受信、検証→内部処理（TCP接続/切断など）→結果生成
- 通知:
  - 処理完了後にのみ`ModelResult`を通知（生JSONの直接通知は行わない）
  - 通知は専用ディスパッチャ経由で発火し、コントローラの処理スレッドから分離

使用例:

```csharp
// 既定（通知なし）
var app = new MyAppMain();
app.Start(5008);

// ビュー（UI）を購読させる
var junction = new AppEventJunction.AppEventJunction();
junction.StartCompleted += result => {/* 更新処理 */};
var app2 = new MyAppMain(junction);
app2.Start(5008);

// 停止
app.Stop();
```

## 依存関係の境界
- `MyAppMain`はASP.NET Coreの抽象化を参照しない（`IServiceCollection`、`WebApplication`などなし）
- 調整はイベントとプレーンDTOのみで行われる
- `AppEventJunction`は同期イベントを提供；`MyAppMain`に注入された場合のみ呼び出す
- TCP接続ロジックは`MyAppMain`内に直接実装されている

## バージョニング
- エンドポイントは`/v1`でグループ化され、`/v2`での将来の破壊的変更を可能にしつつ`/v1`を安定に保つ

## テスト戦略（ブラックボックス）
- テストプロジェクト`MyAppMain.Tests`はMSTestを使用
- `MyAppMain`を空いているポートで開始
- コントローラ（WebAPI）経由でコマンド送信
- `AppEventJunction`の結果イベント`StartCompleted/EndCompleted`を購読し`ModelResult`を検証
- `TcpListener`をポート0にバインドして空いているポートを割り当てるヘルパーを使用

テスト例:

```csharp
[TestMethod]
public async Task Posting_Start_Triggers_External_Handler()
{
    var startTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var junction = new AppEventJunction.AppEventJunction();
    junction.StartRequested += json => startTcs.TrySetResult(json);
    var app = new MyAppMain(junction);
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
- UI購読者の具体化とビジネスロジックの追加
