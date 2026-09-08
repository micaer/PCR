# PCR — 建設ロボット・プログラミングゲーム

初期仕様 v0.1 の第一段階を実装した Headless プロトタイプです。C# がゲーム状態とルールを管理し、ロボットごとに起動した PHP プロセスが JSON Lines で問い合わせ・Action 発行を行います。GUI、Unity、外部 NuGet パッケージは不要です。

## 実行環境

- .NET SDK 9.0（動作確認: 9.0.316）
- PHP CLI 8.2 以上（動作確認: 8.3.7）、`php` が PATH 上にあること
- Windows PowerShell（検証スクリプト用）

Node.js はサンプル JSON を再生成する場合だけ必要です。通常のビルド・テスト・実行には使いません。

## ビルドと検証

リポジトリのルートで実行します。

```powershell
./scripts/verify.ps1
```

ビルド、PHP 構文検証、32 件の Core テストと 15 件の PHP 結合テストを実行します。検証用の .NET/NuGet ディレクトリはリポジトリ内に置き、プロセスの環境変数は終了時に復元します。結合テストには意図的な無限ループの停止確認があるため、最低 5 秒程度かかります。

デモを実行するには、ビルド後に次を実行します。

```powershell
dotnet src/Pcr.Simulator/bin/Debug/net9.0/Pcr.Simulator.dll scenarios/demo.json --output artifacts/demo.json
```

2 台のロボットが別々の PHP プログラムを実行します。Robot01 は「置き場1」への資材運搬・drop・再取得、5 種類の建築、階段の上り下りを行います。Robot02 は現場情報を照会しながら 8 Tick 待機します。現在の標準ライブラリでの完了結果は **283 Tick、5/5 Task 完了**です。

標準 Builder / Carrier の仕事を確認する新しい Scenario:

```powershell
dotnet src/Pcr.Simulator/bin/Debug/net9.0/Pcr.Simulator.dll scenarios/library.json --output artifacts/library.json
```

Builder が WALL / WINDOW の 2 Task を検索して施工し、Carrier が PILLAR 資材を「置き場1」へ保管します。結果は **58 Tick、2/2 Task 完了**です。[標準ライブラリの使い方](docs/standard-library.md)に検索・移動・保管の契約とサンプルの前提を記載しています。

`--php C:/tools/php83/php.exe` で PHP 実行ファイルを、`--runtime /absolute/path/php/runtime.php` で Runtime を指定できます。プログラムのパスは Scenario JSON のディレクトリ基準です。Runtime の既定パスは実行時のカレントディレクトリ基準です。

Core テストだけを実行する場合:

```powershell
dotnet tests/Pcr.Tests/bin/Debug/net9.0/Pcr.Tests.dll
```

## 構成

| 場所 | 役割 |
|---|---|
| `src/Pcr.Core` | Cell、移動、施工、資材、Zone、Tick、競合解決 |
| `src/Pcr.Simulator` | Scenario 読込、PHP プロセス管理、JSONL 通信、実行ログ |
| `php/src/Api.php` | Robot / World API、読み取り用 DTO、例外 |
| `php/src/Library.php` | 拡張可能な Navigation / 各 Manager / Construction |
| `php/programs/demo.php` | 資材運搬から建築までのプログラム例 |
| `php/programs/builder.php`, `carrier.php` | Task 処理ループと単純な配送の標準サンプル |
| `scenarios/demo.json` | 複数階、全建築タイプ、資材、Zone、2 台のロボット |
| `scenarios/library.json` | Builder と Carrier が標準ライブラリで仕事を完了する現場 |
| `tests` | Core テストと実 PHP プロセスの結合テスト |
| `docs/spec-v0.1.md` | 受領した仕様の原文 |
| `docs/design.md` | 座標、競合、通信と今回の解釈 |

## PHP プログラム

プログラムには `$robot` と `$world` が渡されます。Action を呼ぶと Fiber が停止し、ゲーム側の完了応答で再開します。

```php
<?php
use PCR\{ActionException, Direction, Navigation, Heading};

$navigation = new Navigation($robot);
$navigation->turnTo(Heading::EAST);

try {
    if ($robot->scan(Direction::FRONT)->isMovable()) {
        $robot->moveForward();
    }
} catch (ActionException $e) {
    $robot->wait();
}
```

通常の PHP の class / 継承 / interface / trait / enum / namespace / 例外 / `require` が利用できます。Composer のライブラリも、プレイヤー側で用意した `vendor/autoload.php` を `require` して利用する構成です。外部ライブラリ自体は同梱していません。

情報取得は 0 Tick。Action は既定 1 Tick。開始できない Action は 0 Tick の `ActionException`、開始後の競合は Cost 消費後の `ActionInvalidatedException` になります。対象Task自体が完了・消失等で無効になった場合のみ、その派生の `TaskInvalidatedException` になります。継承階層は `ActionException` → `ActionInvalidatedException` → `TaskInvalidatedException` です。`ActionException` は `tick` と `cost` を持ちます。`echo` は stderr に転送され、stdout は Runtime の通信に使います。

## 現段階の範囲

第一段階のルール・Headless 実行・PHP 連携を対象にしています。3D 表示、UI、アニメーション、資材購入や物流経済は未実装です。.NET 9 を選んでおり、Unity 組み込み時には対象フレームワークとアダプターの調整が必要です。

標準 Navigation は局所 scan から探索する基本実装です。最短 Tick の経路、混雑解消、Task 予約・自動役割分担、最寄り資材の到達可能性までは保証しません。`moveAdjacent()` と `Construction::buildAt()` の `floorZ` で作業階を明示できます。複数 Unit の運搬・消費は Core に備えていますが、PHP の `carrying()` は初期版の 1 Block 用です。

PHP はローカルの信頼できるコードを実行する前提です。ゲーム API は状態変更を検証しますが、PHP プロセスそのものを OS レベルで隔離する実装ではありません。ファイルやネットワーク等の通常の PHP 機能は利用可能です。第三者の未信頼スクリプトを受け入れる用途には別途プロセス隔離が必要です。
