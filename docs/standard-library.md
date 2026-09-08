# PHP 標準ライブラリ

この Phase では PHP ライブラリ、読み取り用 DTO、サンプルとテストを更新しています。Core のゲームルール、Primitive Action、World の照会命令、JSONL 通信形式は変更していません。

## Navigation

```php
$navigation = new PCR\Navigation($robot);
$navigation->moveTo(new PCR\Position(3, 0, 1));
$navigation->turnTo(PCR\Heading::WEST);
$navigation->moveAdjacent($material->position(), floorZ: 0);
```

`moveTo()` の Position は立ち位置（床の高さ）です。平地・階段とも scan の `isMovable()` と `destination()` から経路を調べ、実際の移動は `turn()` / `moveForward()` で行います。曲がった後にも scan し直します。経路が見つからなければ `NavigationException`、移動中の競合は wait 後に再探索します。探索は 1 回の `moveTo()` につき最大 4096 ステップです。

探索は既知の有向グラフ上の BFS と未探索セルの訪問です。最短 Tick は保証しません。各移動依頼で探索情報を更新するため、保管先の比較にも移動 Tick がかかります。

```php
class MyNavigation extends PCR\Navigation
{
    protected function findPath(PCR\Position $target): array
    {
        return parent::findPath($target);
    }
}
```

`findPath()` は `[Position, Heading]` の配列を返します。目標がまだ見つからなければ、未探索セルまでの経路でも構いません。`moveTo()` が最初の一歩を検証して実行します。`moveAdjacent()` は指定作業階にある四方の隣接セルを距離、座標キーの順で試し、到着後に対象列を向きます。

## 検索

```php
$tasks = new PCR\TaskManager($world, $robot);
$task = $tasks->findNearest();
$wall = $tasks->findNearest(PCR\BuildType::WALL);

$materials = new PCR\MaterialManager($world, $robot);
$material = $materials->findNearest(PCR\MaterialType::WALL);

$zones = new PCR\ZoneManager($world);
$zone = $zones->findByName('置き場1');
$storageZone = $zones->findByType(PCR\ZoneType::MATERIAL_STORAGE);
```

検索結果は該当なしなら `null` です。Task は PENDING のみ。Material はその時点の World 上の資材のみで、運搬中の資材は含みません。最寄りは現在のロボット位置からの XYZ マンハッタン距離、同距離は ID の文字列順です。これらは経路距離や到達可能性を保証しません。

Zone の同名・同種候補は ID の文字列順で最初の 1 件を返します。既存互換性のため `ZoneInfo::type()` は文字列のままです。`findByType()` には `ZoneType` enum を渡します。

## StorageManager

```php
$storage = new PCR\StorageManager($robot, $navigation);
$storage->store($zone); // 保管先の選択・移動・drop を完了する
```

保管候補は次の順序で選びます。

1. drop できる同種類の山。
2. drop できる空きセル。
3. どちらもなければ `NavigationException`。

Zone セルを探索開始時の距離と座標キーで並べ、実際に隣接セルへ移動して局所 scan で判断します。異種の山、満杯、床・壁・ロボット等で置き場が塞がれたセル、接近できないセルは除外します。床の上に別の階がある場合、遮蔽物より下の操作可能範囲だけを評価します。最後の判定は既存の `drop()` が行い、実行までに状態が変われば別のセルを試します。全候補を試して失敗したときも所持資材を破棄しません。

```php
$cell = $storage->findDropPosition($zone);
if ($cell !== null) {
    $robot->drop();
}
```

`findDropPosition()` は確認のために移動し、選んだセルへ向いた状態で戻ります。0 Tick の検索ではなく、保管先の予約でもありません。利用可能な候補がなければ `null`。所持資材がない場合は `LogicException`、MATERIAL_STORAGE 以外の Zone は `InvalidArgumentException` です。通常は競合後の別候補探索も行う `store()` を利用してください。

保管判断のため、既存 scan 応答を `scan()->space()->cells()` から取得できるようにしました。床上から順に並んだ Cell DTO に対し、`block()` / `material()` / `robot()` / `position()` を参照できます。新しい情報要求や遠方 Cell の取得は行いません。

## Builder / Carrier

`php/programs/builder.php` は未完了 Task を選び、必要資材を検索・取得し、`Construction::buildAt()` で施工します。Task 競合は古い Task を捨てて再検索します。手元に残った資材は次の Task に利用できれば再利用し、種類が違う場合や最後の Task が競合で完了した場合は保管します。

サンプルは地上階（`$workFloor = 0`）、1 Block / 1 Unit の基本作業を対象にしています。必要資材がない場合は診断付きで停止し、一時的な Action 失敗は最大 16 回連続で再試行します。到達不能な現場の自動解決や、異なる階の作業計画は扱いません。

`php/programs/carrier.php` は PILLAR 資材 1 個を検索して「置き場1」へ運ぶだけのサンプルです。継続配送、在庫予約や Builder との通信はありません。

`scenarios/library.json` で両方を同時に実行します。Scenario の再生成は `node scenarios/generate-library.mjs`、通常の実行には Node.js は不要です。既存 demo は引き続き全建築タイプと複数階を検証します。

## 自動検証

`scripts/verify.ps1` に含まれる結合テストで、平地の迂回・到達不能セル・左右回転・Navigation 継承、階段の往復、Task / Material の種類・状態・同距離選択、Zone の名前・種別検索、保管先の優先順と候補除外を確認します。

Builder / Carrier の実プログラムによる完了状態と再実行一致に加え、2 台の Builder が同じ Task を施工する競合と、敗者の余剰資材返却も検証します。CANCELLED Task は現 Core に生成操作がないため、読み取り用 DTO を返すテスト World で標準検索の除外を確認しています。
