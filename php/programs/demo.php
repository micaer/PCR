<?php
declare(strict_types=1);
use PCR\{BuildType, MaterialType, Navigation, TaskManager, MaterialManager, ZoneManager, StorageManager, Construction, Position};

$navigation = new Navigation($robot);
$tasks = new TaskManager($world, $robot);
$materials = new MaterialManager($world, $robot);
$zones = new ZoneManager($world);
$storage = new StorageManager($robot, $navigation);
$construction = new Construction($robot, $navigation);

$zone = $zones->findByName('置き場1') ?? throw new RuntimeException('Missing zone');
$material = $materials->findNearest(MaterialType::WALL) ?? throw new RuntimeException('Missing material');
$navigation->moveAdjacent($material->position(), 0);
$robot->pickup();
$storage->store($zone);
$robot->pickup();
$task = $tasks->findNearest(BuildType::WALL) ?? throw new RuntimeException('Missing task');
$construction->buildAt($task, 0);

foreach ([BuildType::FLOOR, BuildType::PILLAR, BuildType::WINDOW, BuildType::STAIRS] as $type) {
    $task = $tasks->findNearest($type) ?? throw new RuntimeException('Missing task');
    $material = $materials->findNearest($task->requiredMaterial()) ?? throw new RuntimeException('Missing material');
    $navigation->moveAdjacent($material->position(), 0);
    $robot->pickup();
    $construction->buildAt($task, 0);
}
// Follow an existing east-facing stair to a first-floor walkway, then descend.
$navigation->moveTo(new Position(1, 0, 0));
$navigation->moveTo(new Position(3, 0, 1));
$navigation->moveTo(new Position(1, 0, 0));
if (count(array_filter($world->tasks(), static fn($t)=>$t->status()==='PENDING')) !== 0) throw new RuntimeException('Unfinished demo tasks');
