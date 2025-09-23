# IMU コントローラ制御仕様

## 概要
- `MyAppMain` は複数のコントローラ実装を登録できる。
- コントローラは IMU の Start/Stop 操作を提供し、所有権ルールに従う。
- Web API は同時に 1 リクエストのみ処理するため、並列呼び出し時は 429 を返す想定。

## コントローラ登録
- コントローラは共通インターフェース（仮称 `IMyAppController`）を実装する。
- 同じインスタンスを重複登録した場合は無視する。
- 登録解除時は IMU の停止を試みず、所有権のみをクリアする。

## Start/Stop の結果型
- Start/Stop は共通の結果型を返す。
  - `enum ImuControlStatus { Success, AlreadyRunning, OwnershipError, Failed }`
  - `string Message` で詳細を補足する。
- 例外は想定外の致命的エラーに限定し、所有権エラーなどは結果型で表現する。

## 所有権ルール
- Start に成功したコントローラがオーナーとなる。
- オーナーが登録解除または破棄された場合、所有権は未設定状態に戻る。
- 所有権未設定状態では誰でも Stop を呼び出せる。

## Start 操作
- IMU 停止中に Start → 成功し、呼び出し元が新しいオーナーになる。
- すでに起動中でオーナーが Start → 冪等に成功扱い（`AlreadyRunning` または `Success`）。
- すでに起動中で別コントローラが Start → `OwnershipError` を返す。
- Start が内部エラーで失敗した場合は所有権を変更せず、`Failed` を返す。
- 所有権未設定状態（前オーナー解除後など）で Start → 成功し、新しいオーナーを設定する。

## Stop 操作
- オーナーが Stop → 成功し、所有権を未設定に戻す。
- IMU が既に停止している場合でも成功扱い。
- 所有権未設定状態で Stop → 誰でも成功扱い。
- オーナー以外が Stop → `OwnershipError`。
- Stop 失敗時は所有権を維持し、`Failed` を返す。

## 並行制御
- Start/Stop の所有権判定と更新は排他的に処理する。
- CommandPipeline が順次処理するため、明示的なロックを追加しなくても仕様を満たす前提。
- 非同期操作中の別リクエストは CommandPipeline で待機し、Web API レベルでは 429 が返る。

## ログと診断
- 所有権エラーや失敗時にはログで呼び出し元と状態を記録する。
- 所有権の問い合わせ API は提供しない。

## テスト戦略
- `MyAppMainDirectApiControllerTests` で直接制御コントローラの挙動を検証し、成功パス・所有権競合・登録解除後の再取得までカバーする。
- 既存の Web API 経由のブラックボックス テストで CommandPipeline 経由の処理順序とレートリミットを担保する。
- 将来的に新しいコントローラを追加する場合は、所有権エラーや冪等性を確認するユニットテストを同等に追加する。
- IMU クライアントの統合テストはテストサーバ（`TestImuServer`）を用い、ON/OFF の通知順とサンプル配信を継続的にモニターする。

## 拡張の扱い
- 優先度やタイムアウト等の将来拡張は今回考慮しない。
- 仕様変更時には本書を更新すること。

## 実装メモ
- Web API 以外から直接制御したい場合は `DirectApiController` を利用できる。
- `StartImu` / `StartImuAsync` および `StopImu` / `StopImuAsync` は要求を受け付けたかどうか（`bool`）のみ返し、詳細な結果は `MyNotificationHub.ResultPublished` で購読する。
  - `MyAppMain.RegisterController` 後に呼び出すことで、CommandPipeline の逐次処理と所有権ルールの統一を保つ。
