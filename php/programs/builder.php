<?php
declare(strict_types=1);
use PCR\{ActionException, TaskInvalidatedException, Navigation, TaskManager, MaterialManager,
    ZoneManager, StorageManager, Construction};

$navigation = new Navigation($robot);
$tasks = new TaskManager($world, $robot);
$materials = new MaterialManager($world, $robot);
$storage = new StorageManager($robot, $navigation);
$construction = new Construction($robot, $navigation);
$zone = (new ZoneManager($world))->findByName('置き場1') ?? throw new RuntimeException('Missing storage zone');

// This introductory builder works on the ground floor. Navigation itself supports stairs.
$workFloor = 0;
$failures = 0;
while (($task = $tasks->findNearest()) !== null) {
    try {
        // A lost task may leave an unused block. Reuse it, or return it to storage.
        $held = $robot->carrying();
        if ($held !== null && $held->type() !== $task->requiredMaterial()) {
            $storage->store($zone);
        }
        if ($robot->carrying() === null) {
            $material = $materials->findNearest($task->requiredMaterial())
                ?? throw new RuntimeException('No material for task '.$task->id());
            $navigation->moveAdjacent($material->position(), $workFloor);
            $robot->pickup();
        }
        $construction->buildAt($task, $workFloor);
        $failures = 0;
    } catch (TaskInvalidatedException $e) {
        // Discard the old Task snapshot and select again from the current World.
        continue;
    } catch (ActionException $e) {
        // Short-lived movement/material/build conflicts may be retried.
        if (++$failures >= 16) throw new RuntimeException('Builder cannot make progress', previous: $e);
        $robot->wait();
    }
}
// Return any block left by the final competing build.
if ($robot->carrying() !== null) $storage->store($zone);
