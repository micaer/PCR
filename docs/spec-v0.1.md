# 建設ロボット・プログラミングゲーム 初期仕様 v0.1

## 1. コンセプト

プレイヤーがロボット用プログラムを書き、複数のロボットに資材運搬・建築などを自動実行させる建設シミュレーションゲーム。

HR2のような「ロボットをプログラムして建築させる」楽しさを核としつつ、スクリプト環境は現代的なものにする。

特に以下を重視する。

* スクリプトの行数制限を設けない
* 外部ライブラリ・複数ファイルを利用可能にする
* クラス・継承・Interface・Trait・enum・例外処理などを利用可能にする
* 標準ライブラリだけでも十分遊べる
* 高度な自作コードにより、同じゲームルール内でより効率的な動作を実現可能
* 低レベルAPIを利用してもゲームルールそのものを突破することはできない
* 高度なプログラミングができなくてもゲーム上不利にならない
* ゲームロジックと描画を分離し、CodexがGUIを操作しなくても大半を開発・テストできる構造にする

---

# 2. スクリプト言語

初期候補は PHP。

ゲーム本体とは独立したPHP Runtimeとして動作させることを基本方針とする。

PHP側では通常のPHP機能を利用可能とする。

例：

* class
* inheritance
* interface
* trait
* enum
* namespace
* exception
* 複数ファイル
* 外部ライブラリ

ロボットの実行状態については、Fiber等を利用し、ゲームAction発行時にスクリプト実行を停止し、Action完了後に続きを再開できる構造を想定する。

具体的なPHP Runtimeとの通信方式は実装フェーズで決定する。

---

# 3. ゲーム時間とTick

ゲーム世界はTick単位で進行する。

通常のPHPコードそのものはゲーム時間を消費しない。

```php
$task = $tasks->find();

if ($task !== null) {
    // ここは0 tick
}
```

ゲーム側が公開するAction系APIを実行した場合のみ、設定されたAction Costを消費する。

初期版ではすべてのAction Costを原則1 tickとする。

```php
$robot->turn(TurnDirection::RIGHT); // 1 tick
$robot->moveForward();              // 1 tick
$robot->pickup();                   // 1 tick
$robot->build();                    // 1 tick
```

将来的には、

* Action種別
* ロボット性能
* 建築物
* 荷物
* 地形

等に応じて異なるCostを設定可能な構造とする。

---

# 4. Robot 最小API

初期公開APIは可能な限り少なくする。

## Action系

```php
$robot->moveForward();

$robot->turn(TurnDirection::LEFT);
$robot->turn(TurnDirection::RIGHT);

$robot->pickup();
$robot->drop();
$robot->build();

$robot->wait();
```

初期版ではすべて1 tick。

## 情報取得系

```php
$robot->position();
$robot->direction();
$robot->carrying();

$robot->scan(Direction::FRONT);
```

情報取得は原則0 tick。

---

# 5. turn()

低レベル命令として左右回転のみ提供する。

```php
enum TurnDirection
{
    case LEFT;
    case RIGHT;
}
```

180度回転したい場合は2回実行する。

```php
$robot->turn(TurnDirection::RIGHT);
$robot->turn(TurnDirection::RIGHT);
```

便利な `turnTo()` 等は標準Navigationライブラリ側で実装する。

---

# 6. scan()

```php
$scan = $robot->scan(Direction::FRONT);
```

指定方向へ1マス進む際に関係する縦方向の空間情報を取得する。

初期設定上の最大高さは4。

内部的には概ね、

```text
+4
+3
+2
+1
+0  現在階と同じ高さの床
-1  下り階段等の判定用
```

を調査する。

ユーザー向けには主に、

```php
$scan->isMovable();

$scan->floor();
$scan->space();

$scan->destination();
$scan->elevationDelta();
```

などを提供する。

---

# 7. floor()

`scan()->floor()` は、現在階と同じ高さにある床Cellの情報を表す。

```php
$scan->floor()->block();
$scan->floor()->task();
$scan->floor()->zone();
```

---

# 8. space()

`scan()->space()` は、床上の空間をまとめて表す。

主に高さ+1～+4を扱う。

```php
$scan->space()->robot();
$scan->space()->materials();
$scan->space()->tasks();
```

内部では各高さのCell情報を個別に保持する。

将来的な低レベルAPIでは、

```php
$scan->cellAt(-1);
$scan->cellAt(0);
$scan->cellAt(1);
$scan->cellAt(2);
$scan->cellAt(3);
$scan->cellAt(4);
```

などを公開可能な構造としておく。

---

# 9. isMovable()

```php
$scan->isMovable();
```

は、

「現在のロボットが、この方向へ `moveForward()` を1回実行可能か」

を意味する。

単純な空きマス判定ではない。

平地・上り階段・下り階段等をゲーム側で判定する。

例：

```text
平地
(x,y,z) → (x+1,y,z)

上り階段
(x,y,z) → (x+1,y,z+1)

下り階段
(x,y,z) → (x+1,y,z-1)
```

移動可能であれば、

```php
$scan->destination();
$scan->elevationDelta();
```

から移動先情報を取得可能とする。

`isMovable()` は現在時点での判定であり、その後の移動成功を予約・保証するものではない。

他ロボット等により状況が変化した場合、実際の `moveForward()` は失敗する可能性がある。

---

# 10. 初期建築タイプ

初期版では少なくとも以下を扱う。

```text
FLOOR
WALL
PILLAR
WINDOW
STAIRS
```

階段は方向を持つ。

階段上でも通常の、

```php
$robot->moveForward();
```

を利用する。

階段専用移動命令は設けない。

将来追加候補：

* 柵
* 門
* ドア
* 手すり
* 梁
* その他設備

---

# 11. build()

```php
$robot->build();
```

ロボット正面に存在するBuild Taskのうち、

1. 現在所持しているMaterialで建築可能
2. 現在階から施工可能
3. その他の建築条件を満たしている

Taskを探す。

複数存在する場合は、建築可能なもののうち最も低い高さから処理する。

例：

```text
+4 WALL
+3 WINDOW
+2 WALL
+1 PILLAR
+0 FLOOR
```

WALL資材を持っている場合は+2を建築する。

---

# 12. build() の施工可能高さ

現在ロボットが立っている床から、次に存在する上階の床までを「現在階の施工範囲」とする。

```text
+4  上階側
+3  FLOOR ← 現在階の施工上限
+2  WALL  ← 下階から建築可能
+1  WALL  ← 下階から建築可能
+0  FLOOR ← ロボットの現在階
```

上階の床がすでに完成していても、その床より下の現在階を構成する壁・柱・窓等は建築可能。

その床より上のTaskは下階から建築できない。

これにより、

* 壁を先に作る
* 床を先に作る
* 床担当と壁担当を並行動作させる

など複数の建築順序を許容する。

---

# 13. pickup()

```php
$robot->pickup();
```

正面の操作可能範囲に存在するMaterialBlockを拾う。

対象は未使用の資材のみ。

建築済みの、

* FLOOR
* WALL
* PILLAR
* WINDOW
* STAIRS

等はpickupできない。

資材が複数段積まれている場合は、一番上のMaterialBlockを取得する。

例：

```text
+4 空
+3 WALL
+2 WALL
+1 WALL
+0 FLOOR
```

`pickup()` を実行すると+3のWALLを取得する。

途中に床等の遮蔽物が存在する場合、その上側にはアクセスできない。

```text
+4 MATERIAL ← pickup不可
+3 FLOOR    ← ここで遮断
+2 MATERIAL ← pickup可能
+1 MATERIAL ← pickup可能
+0 FLOOR    ← ロボットの現在階
```

---

# 14. drop()

```php
$robot->drop();
```

正面の操作可能範囲へ、現在所持しているMaterialBlockを置く。

資材は低い位置から積まれる。

```text
+3 空
+2 WALL
+1 WALL
+0 FLOOR
```

WALLをdropすると+3へ置かれる。

---

# 15. 資材の山

正面にすでにMaterialBlockが存在する場合、同じ種類のMaterialのみ追加可能。

```text
WALL
WALL
WALL
FLOOR
```

にはWALLのみdrop可能。

異なる種類の資材を1つの山へ混在させない。

床等の遮蔽物を飛び越えて資材を置くことはできない。

---

# 16. MaterialBlock と Material Unit

物理的にゲーム世界へ存在する資材と、建築に消費される量を分離する。

## MaterialBlock

ロボットがpickup / dropして物理的に運搬するオブジェクト。

## Material Unit

建築時に消費される量。

初期版では、

```text
MaterialBlock 1個 = 1 Unit
Build Task 1箇所 = 1 Unit
```

とする。

ただし内部データでは独立して扱う。

将来的には、

```text
MaterialBlock 1個 = 4 Unit
WALL 1箇所 = 1 Unit
```

等へ変更可能な構造とする。

---

# 17. Robot Inventory / Carry Capacity

初期版ではロボットはMaterialBlockを1個だけ運搬可能。

ただし内部では固定個数ではなくUnit単位のCapacityとして扱う。

初期値：

```text
Robot Capacity = 1 Unit
MaterialBlock = 1 Unit
```

将来的には、

```text
Robot Capacity = 8 Unit
MaterialBlock A = 2 Unit
MaterialBlock B = 4 Unit
```

等へ拡張可能な構造としておく。

初期ゲーム上では、この拡張性をプレイヤーへ露出させる必要はない。

---

# 18. Zone

Zoneは初期版から実装する。

Zoneは建築物ではなく、複数の床Cellへ付与できる論理的なエリア情報。

例：

```text
Zone #1
type: MATERIAL_STORAGE
name: "置き場1"

cells:
  (10,20,0)
  (11,20,0)
  (12,20,0)
  (13,20,0)
```

床自体は通常のFLOOR。

Zoneは用途・名前・まとまりを表現する。

基本情報：

```php
$zone->id();
$zone->name();
$zone->type();
$zone->cells();
```

初期用途：

```text
MATERIAL_STORAGE
```

将来的には、

```text
WORK_AREA
STAGING_AREA
WAITING_AREA
DELIVERY_AREA
```

等を追加可能。

`drop()` 自体はZoneを意識しない。

`drop()` は物理的に置けるかだけを判定する。

「置き場1へ資材を運ぶ」といった判断はRobot Programまたは標準LibraryがZone情報を利用して行う。

---

# 19. World API

ロボットは、自身の周囲を取得する `scan()` とは別に、現場全体の管理情報を取得するための `World` APIを利用できる。

World APIによる情報取得は原則0 tickとする。

初期実装では少なくとも以下を提供する。

```php
$world->tasks();
$world->zones();
$world->materials();
$world->robots();
```

World APIは原則として「生の情報取得」を担当する。

検索・優先順位・最寄り選択等の便利処理は標準ライブラリ側へ実装する。

---

# 20. World::tasks()

現在存在する建築Task等の一覧を取得する。

各Taskから少なくとも以下を取得可能とする。

```php
$task->id();
$task->position();
$task->type();
$task->status();

$task->requiredMaterial();
$task->requiredUnits();
```

便利なTask検索は標準 `TaskManager` 側へ実装する。

例：

```php
$task = $taskManager->findNearest(
    BuildType::WALL
);
```

---

# 21. World::zones()

現在定義されているZone一覧を取得する。

```php
$zone->id();
$zone->name();
$zone->type();
$zone->cells();
```

Zone検索は標準ライブラリ側へ実装する。

例：

```php
$zone = $zoneManager->findByName('置き場1');
```

---

# 22. World::materials()

ゲーム世界上に存在するMaterialBlock情報を取得する。

少なくとも、

```php
$material->id();
$material->position();
$material->type();
$material->units();
```

を取得可能とする。

最寄り資材検索等は `MaterialManager` 側へ実装する。

例：

```php
$material = $materialManager->findNearest(
    MaterialType::WALL
);
```

---

# 23. World::robots()

ゲーム世界上のロボット情報を取得する。

少なくとも、

```php
$robotInfo->id();
$robotInfo->position();
$robotInfo->direction();
$robotInfo->status();
```

を取得可能とする。

これにより高度なスクリプトでは、

* Task競合回避
* 混雑回避
* 独自の役割分担
* 他ロボットとの協調
* 独自の建築・配送スケジューリング

等を実装可能とする。

ただし、他ロボットを直接操作する機能は提供しない。

---

# 24. World と scan() の役割

両者の用途を明確に分ける。

## World

現場管理システムとして取得可能なグローバル情報。

例：

* 建築予定
* Zone
* 資材位置
* ロボット位置

## scan()

ロボット自身の現在位置から見た局所的・物理的な情報。

例：

* 正面へ実際に移動可能か
* 正面の床
* 正面の空間
* 正面に積まれている資材
* 階段による移動先

したがって、

「遠方にTaskが存在する」

ことはWorldから確認可能でも、

「次の1マスへ実際に進める」

かどうかは `scan()` / `isMovable()` により判断する。

---

# 25. 標準ライブラリ

ゲーム本体は最低限のPrimitive APIのみ提供する。

便利機能はPHP標準ライブラリとして提供する。

初期候補：

```text
Navigation
TaskManager
MaterialManager
ZoneManager
StorageManager
Construction
RobotProgram
```

例えば、

```php
$navigation->moveTo($target);
```

はゲーム本体の特権命令ではない。

内部では、

```php
$robot->turn(...);
$robot->moveForward();
```

等のPrimitive APIを組み合わせて実装する。

標準Libraryもプレイヤーと同じゲームルールに従う。

---

# 26. 標準ライブラリの拡張

標準Libraryは継承・差し替え可能とする。

例：

```php
class MyNavigation extends Navigation
{
    protected function findPath(Position $target): array
    {
        // 独自経路探索
    }
}
```

高度なプレイヤーは、

* 階段を避ける
* 混雑を避ける
* 曲がる回数を減らす
* 荷物によって経路を変更する
* 他ロボットとの競合を避ける

等を実装可能。

標準Libraryを自作できなくてもゲーム上不利にならないことを原則とする。

---

# 27. 低レベルAPI

初期版では必要最低限のみ公開する。

ただし内部構造は、将来的に低レベルAPIを公開可能な設計とする。

低レベルAPIを使用しても、

```text
壁を通過する
瞬間移動する
Action Costを無視する
本来取得できない情報を見る
建築条件を無視する
```

等は不可能。

低レベルAPIの目的は、

「同じルールの中で、より良い判断・効率化・高度な役割分担を実現する」

ことである。

能力差ではなく、知恵やアルゴリズムによる効率差を許容する。

---

# 28. Task競合と世界状態変化

複数ロボットが並行して動作するため、

```text
移動中に別ロボットがTaskを完了
移動中に経路が塞がる
資材を他ロボットが取得
建築Taskが実行不能になる
```

等が発生する。

Action開始前に実行不能である場合は即座に失敗可能。

Action開始後に世界状態が変化した場合は、Action失敗としてRobot Scriptへ通知する。

PHP側ではExceptionとして扱える設計を想定する。

例：

```php
try {
    $navigation->moveTo($task->position());
    $robot->build();

} catch (TaskInvalidatedException $e) {
    // 次のTaskを探す
}
```

例外処理・Task再検索等そのものは0 tick。

次のActionを発行した時点で再びゲーム時間を消費する。

---

# 29. 失敗時のTick

基本方針：

## Actionを開始できない

0 tickで失敗可能。

例：

```text
pickupしたが最初から資材がない
dropしたが最初から置けない
buildしたが対応Taskがない
```

## Action開始後に状況が変化した

Action Costは消費する。

例：

```text
build開始
↓
別ロボットがTask完了
↓
Action失敗
```

競合による時間損失はゲーム上発生しうる。

---

# 30. 初期版で扱わないもの

以下は後回しとする。

```text
資材購入
資材価格
予算
資材搬入口
発注
納期
詳細な物流経済
MaterialBlockあたり複数Unitの実ゲーム利用
複数Unit容量ロボットの実ゲーム利用
柵
門
ドア
その他高度な設備
```

データ構造上の拡張性だけ確保する。

---

# 31. Codex開発コストを抑えるための設計原則

今回の重要要件。

## ゲームロジックとGUIを分離する

ゲームルールをUnity等の描画環境へ直接依存させない。

```text
Core Simulation
PHP Runtime
Tests
Headless Simulator

        ↑

Unity / UI / Rendering
```

の構造を目標とする。

---

# 32. Headless Simulator

GUIなしでゲームシミュレーションを実行可能にする。

例：

```text
simulator scenario.json
```

結果：

```text
Tick 10 Robot01 TURN_RIGHT
Tick 11 Robot01 MOVE_FORWARD
Tick 12 Robot02 BUILD Task#8
Tick 13 Robot01 TaskInvalidated
```

---

# 33. 決定論的シミュレーション

同じ、

* 初期状態
* Script
* Seed

であれば、原則として同じ結果になること。

不具合を再現用Scenarioとして保存できる構造にする。

---

# 34. 自動テスト

重要なゲームルールはGUIなしで検証可能にする。

例：

```text
MoveForward costs 1 tick
Turn changes direction correctly

Pickup selects top material
Pickup cannot cross a floor

Drop stacks same material
Drop rejects mixed materials
Drop cannot cross a floor

Build selects lowest compatible task
Build can construct below an already-built upper floor
Build cannot construct above the next floor

Stairs resolve correct destination
Down stairs require lower-cell information

Zone membership is returned correctly

World returns tasks
World returns zones
World returns materials
World returns robots
```

---

# 35. Codexへのデバッグ依頼方針

可能な限り、

```text
再現テスト作成
↓
テスト失敗確認
↓
修正
↓
全テスト成功
```

で完結させる。

Codexにゲーム画面を何度も起動・確認させない。

GUI確認が必要なのは主に、

```text
表示
カメラ
操作感
レイアウト
アニメーション
```

へ限定する。

ゲームロジックの不具合は原則Headless Simulatorまたは単体テストで再現する。

---

# 36. 初期プロトタイプの完成条件

第一段階では大規模なゲーム完成を目標にしない。

以下がHeadless Simulator上で動作すれば第一段階成功とする。

```text
簡単な複数階マップ

↓

FLOOR / WALL / PILLAR / WINDOW / STAIRS

↓

複数種類のMaterialBlock

↓

Zone
  MATERIAL_STORAGE
  "置き場1"

↓

Robot 1台以上

↓

PHP Script実行

↓

World APIから
Task / Zone / Materialを検索

↓

scan

↓

turn / moveForward

↓

pickup

↓

ZoneまでNavigationで移動

↓

drop

↓

建築Taskを検索

↓

Navigationで移動

↓

build
```

この一連がtick単位で正しく動作し、自動テストで検証可能であること。

Unity等による3D表示は、その後に追加する。

---

# 37. 初期開発時の基本方針

実装開始時点では、ゲーム画面よりも以下を優先する。

1. Core Simulation
2. Tick / Action
3. World / Cell / Floor
4. Robot
5. Material
6. Task
7. Zone
8. scan / Movement
9. build / pickup / drop
10. Headless Simulator
11. PHP Runtime
12. 標準Library
13. 自動テスト
14. 最後にUnity等のGUI

最初からGUI中心の実装にはしない。

Codexがコード・テスト・Scenarioだけで大半の開発とデバッグを完結できる構造を維持する。
