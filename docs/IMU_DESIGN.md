# IMU サーバ連携: プロトコルと設計（テスト用）

## 目的
- WebAPI の `start` で IMU データ配信を開始し、TCP サーバへ接続する。
- 接続・状態・データを `MyAppNotificationHub` に DTO で通知する。
- IMU サーバが `OFF` の場合はクライアント（本アプリ）から `ON` 要求を送る。
- テストでは「`ON` 以外の状態通知を無視する」ことを確認する。

## プロトコル仕様（テスト用 IMU サーバ）
- エンディアン: Little-endian
- ヘッダー: [MessageId:uint8][PayloadSize:uint32]
  - `MessageId`: 1 バイトの符号なし整数
  - `PayloadSize`: 4 バイトの符号なし整数（ペイロードのバイト数）
- ペイロード: ヘッダーに続く Raw バイナリ

### メッセージ ID 割り当て
- `0x01` = IMU_STATE（サーバ→クライアント）
  - ペイロード: `State:uint8`（0=OFF, 1=ON）
  - 新規クライアント接続時に現状態を即時送信。状態変更時は全クライアントにブロードキャスト。
- `0x02` = IMU_DATA（サーバ→クライアント）
  - ペイロード（合計 8 + 4*6 = 32 バイト）
    - `TimestampNs:uint64`（UTC 時刻 ns）
    - `GyroX/Y/Z:float`（rad/s）
    - `AccelX/Y/Z:float`（m/s^2）
  - 配信条件: IMU が `ON` のとき 100Hz で全クライアントへ配信
- `0x81` = SET_IMU_STATE（クライアント→サーバ）
  - ペイロード: `State:uint8`（0=OFF, 1=ON）

## テスト用 IMU サーバ設計（`MyAppMain.Tests` 内）
- クラス: `TestImuServer`
- 状態
  - `imuOn: bool`（初期 `false` = OFF）
  - `clients: List<TcpClient>`（接続クライアント管理）
  - `cts: CancellationTokenSource`（終了制御）
  - `broadcasterTask: Task`（100Hz データ送信ループ）
- 受入
  - `AcceptTcpClientAsync` でクライアント接続を受理 → `clients` に登録
  - 登録直後、そのクライアントに `IMU_STATE`（現状態）を送信
- 受信処理（クライアント毎）
  - `SET_IMU_STATE` を受けたら `imuOn` を更新
  - 状態が変化したら全クライアントに `IMU_STATE` をブロードキャスト
- データ配信
  - `imuOn == true` の間、100Hz で `IMU_DATA` を全クライアントへ送信
  - `TimestampNs` は `DateTime.UtcNow` → ns 変換、`Gyro/Accel` はダミーでも可
- 終了
  - `cts.Cancel()` → クライアント全切断、リソース解放

## NotificationHub（`MyAppNotificationHub`）の拡張
- DTO を追加（例）
  - `ImuConnectionChangedDto { bool Connected; string? RemoteEndPoint; }`
  - `ImuStateChangedDto { bool IsOn; }`
  - `ImuSampleDto { ulong TimestampNs; (float X,float Y,float Z) Gyro; (float X,float Y,float Z) Accel; }`
- イベントを追加（同期）
  - `event Action<ImuConnectionChangedDto>? ImuConnected;`
  - `event Action<ImuConnectionChangedDto>? ImuDisconnected;`
  - `event Action<ImuStateChangedDto>? ImuStateUpdated;`
  - `event Action<ImuSampleDto>? ImuSampleReceived;`
- 備考: 既存の `StartCompleted/EndCompleted` は維持。IMU 系は別系統の通知。

## MyAppMain の拡張
- 接続フロー（WebAPI `start` → 接続情報受領 → TCP 接続）
  1. コマンド処理レイヤー (`CommandHandler`) が接続情報を取り出し、`ImuClient` に委譲
  2. `ImuClient` が TCP 接続し、成功時に `ImuConnected` を通知
  3. `ImuClient` 内部で受信ループを開始
- 受信ループ（`ImuClient` 内）
  - ヘッダー（1+4 バイト）を読み、メッセージ単位で分岐
  - `IMU_STATE`
    - `State` の値にかかわらず `ImuStateUpdated(IsOn)` を通知
    - `State == 0(OFF)` のときは通知後に `SET_IMU_STATE(ON)` を送信して再開要求
  - `IMU_DATA`
    - ペイロードを `ImuSampleDto` に変換して `ImuSampleReceived` を通知
  - 切断時は `ImuDisconnected` を通知
- 送信ヘルパー
  - `ImuClient` が `SET_IMU_STATE` を送信（`SendImuOnOffRequest` 相当）
- チャネル処理
  - `CommandPipeline` が非同期チャネル (`Channel<ModelCommand>`/`Channel<ModelResult>`) を処理し、結果を `MyAppNotificationHub` へ通知
- 安全性
  - ストリーム読み取りは `ReadExactAsync` で所定バイト数を必ず読む
  - 例外はループを終了し切断へ

## テスト戦略（`MyAppMainBlackBoxTests` の拡張）
- 変更対象: `Start_Message_With_Server_Info_Connects_To_TCP_Server`
- 手順
  1. WebAPI `POST /v1/start` を実行し `200 OK` を確認
  2. `ImuConnected` 通知を待機
  3. `ImuStateUpdated`(ON) 通知を待機
     - ここで OFF 通知を無視すること（テスト側では OFF を受けても完了にしない）
  4. `ImuSampleReceived` を 1 件以上受信
  5. WebAPI `POST /v1/end` で終了
- 成功条件
  - `200 OK`、`ImuConnected`、`ImuStateUpdated(ON)`、`ImuSampleReceived>=1` が満たされること

## 型・バイト配列の詳細
- Little-endian でエンコード/デコード
- `float` は IEEE754 単精度（4 バイト）
- `TimestampNs` は `ulong`（8 バイト）
- ヘッダー長は固定 5 バイト（ID:1 + Size:4）

## 将来拡張の考慮
- サンプルフォーマットの拡張（温度/磁気など）→ 新 ID の追加で後方互換
- 周波数制御・負荷抑制（サンプリング間引き・バッファリング）
- 再送/欠落処理（テストでは非考慮）
- 認証/暗号化（本番想定では TLS や署名を検討）

## 実装順序（このドキュメントに基づく）
1. `MyAppNotificationHub` に DTO とイベントを追加
2. テスト用 `TestImuServer` を `MyAppMain.Tests` に実装
3. `MyAppMain` に受信ループ/送信ヘルパーを実装
4. 既存テストの拡張（接続/ON 確認/サンプル受信/終了）
5. ビルド・テスト
