# 実装上の取り決め

仕様で詳細が未確定の箇所について、プロトタイプでは以下を採用しています。

## Core と Runtime の境界

Core は PHP・プロセス・GUI に依存しません。外部から呼べる状態変更は `Begin()` と `Advance()` です。World と scan の戻り値はスナップショットで、配列を書き換えても Core は変化しません。

Simulator が PHP プロセスとロボット ID を結び付けます。PHP のメッセージに別 ID を指定しても操作対象は変わりません。任意座標の移動や状態書き換えを行う通信命令は公開しません。

PHP のクラスを継承・改変した場合も、ゲーム状態を変更するには同じ Primitive Action の検証を通ります。ただしこれはゲーム内の権限境界であり、OS 上の悪意あるコードに対するサンドボックスではありません。

## Tick と競合

1. ロボット ID の ordinal 昇順で、各プログラムを次の Action または終了まで実行する。
2. 照会・PHP 演算・開始時失敗は 0 Tick。Action が受理されると完了予定 Tick と対象 ID・位置を保存する。
3. 待機中 Action がある場合に世界を 1 Tick 進める。
4. 完了予定になった Action をロボット ID 順で再検証・適用する。
5. 全完了処理後、次のループで PHP が再開する。

同一セルへの移動は先に確定したロボットが成功します。移動開始時に占有されているセルには予約できないため、位置交換は失敗します。同じ資材や Task を狙った後続ロボットは失敗し、次の候補へ自動的に切り替えません。成功・失敗とも、開始済みなら設定 Cost を消費します。

`actionCosts` のキーは `MOVE_FORWARD`, `TURN_LEFT`, `TURN_RIGHT`, `PICKUP`, `DROP`, `BUILD`, `WAIT`、値は 1 以上の整数です。初期値はすべて 1。

Seed は各 PHP プロセスの `mt_srand()` に渡します。Core は乱数を利用しません。同じ PHP バージョン・コード・入力に対する再現性をテストしています。PHP の時刻、`random_int()`、外部ファイルやネットワークの変化まで決定論化するものではありません。

## 座標と階段

X は東に増加、Y は南に増加、Z は上に増加します。Robot Position の Z は立っている床の高さです。ロボット本体はその +1 Cell を占有します。現時点の本体高さは 1 Cell です。MaxHeight=4 は世界全体の上限ではなく、相対 scan / 施工 / 資材操作範囲の上限です。

東向き階段の例:

```text
FLOOR (0,0,0) → STAIRS (1,0,1,EAST) → FLOOR (2,0,1)
立ち位置   z=0              z=1                  z=1
```

階段 Cell 自体の上面を床として扱います。階段へ下から入るときは向きが一致する場合に限り +1。階段上から下り方向へ出るときは、正面 -1 の支持床を確認して -1 へ移動します。それ以外は同じ高さの支持床へ移動します。移動先のロボット・資材・壁等で塞がれていれば失敗します。

`scan()` は正面の -1 から +MaxHeight の Cell を取得し、`floor()` は +0、`space()` は +1 以上をまとめます。階段の実際の移動先は `destination()` を利用してください。

## 施工と資材

施工範囲は正面の列の +0 から、次の FLOOR（完成済みまたは予定）を含む高さまでです。上階 FLOOR がなければ +MaxHeight までです。予定床も階の境界とすることで、建築順序によって上階 Task が下階から施工可能になることを防ぎます。

施工は資材の種類・量、範囲、対象 Cell の占有を確認し、最も低い適合 Task を選びます。間の壁や柱の完成順序で施工を塞がず、上階 FLOOR の下にある壁等は施工できます。構造力学や隣接支持などの追加建築条件は現段階ではありません。

pickup / drop の操作範囲は正面 +1 から最初の完成 Block またはロボット直前までです。床以外の固体 Block も遮断します。pickup は範囲内の最上段、drop は同種類の山の上に置きます。drop は現在階の支持床を必要とし、範囲を越えた位置や別の床の上には置きません。未施工 Task は物理的障害物ではありません。

MaterialBlock の ID を維持して運搬します。`units` と `requiredUnits`、Robot の `capacity` は独立しています。建築後に残量がある場合は所持したままです。World の materials は床上の資材のみで、運搬中の資材は含みません。World の tasks は完了 Task も返し、Manager は PENDING だけを検索します。

Zone は完成 FLOOR Cell に付与します。v0.1 では 1 Cell につき 1 Zone、type は MATERIAL_STORAGE。drop は Zone 所属を条件にしません。

## 通信

stdin / stdout の UTF-8 JSON Lines、1 リクエストにつき 1 応答です。

```json
{"op":"query","query":"scan","direction":"FRONT"}
{"op":"action","action":"MOVE_FORWARD"}
```

照会には `robot`, `tasks`, `zones`, `materials`, `robots`, `scan` を利用できます。情報は `{"ok":true,"data":...,"tick":0}`、Action は `{"ok":true,"error":null,"tick":1,"cost":1}` の形で返します。終了時は `{"op":"done"}`、スクリプト例外は `{"op":"error","message":"..."}` を Runtime が送信します。

ロボットごとに 1 PHP プロセス、1 Fiber を使用します。ユーザープログラム中の Primitive Action が Fiber を suspend し、Runtime が Action を通信して応答で resume します。普通の PHP コードや World 照会は Fiber 内で続行します。

応答待ち・入力送信は 5 秒、リクエストは 1 メッセージ 1 MiB まで、次の受理 Action まで 10,000 リクエスト、Scenario は `maxTicks` までに制限しています。違反や未処理例外はそのシミュレーションを停止し、子プロセスを終了します。これらはホストの実行制限で、ゲームの Tick やスクリプト行数制限ではありません。

## 再現・デバッグ

ゲームルールのバグは `tests/Pcr.Tests/Program.cs` に Scenario と操作列のテストを追加して再現します。通信・PHP 側の問題は `tests/fixtures` と `Integration.cs` で実プロセスを検証します。失敗テスト確認後に修正し、`scripts/verify.ps1` で全体を確認します。

Simulator の `--output` は最終世界状態と Action ログ（Tick、Cost、成功/失敗、対象 ID・座標）を JSON に保存します。現在の出力は途中からの PHP Fiber 再開用セーブデータではありません。不具合の再実行には初期 Scenario と使用 PHP ファイルを保存してください。
