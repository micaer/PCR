<?php
declare(strict_types=1);
use PCR\{ActionException, ActionInvalidatedException, TaskInvalidatedException};

// Obtain separate blocks from the same stack before issuing concurrent builds.
if ($robot->position()->x === 0) {
    $robot->pickup();
    $robot->wait();
} else {
    $robot->wait();
    $robot->pickup();
}
try {
    $robot->build();
} catch (TaskInvalidatedException $e) {
    if (get_class($e) !== TaskInvalidatedException::class || $e->tick !== 3 || $e->cost !== 1) {
        throw new RuntimeException('Wrong task conflict exception or cost');
    }
    try { throw $e; }
    catch (ActionInvalidatedException $invalidated) {
        if ($invalidated !== $e) throw new RuntimeException('Exception identity changed');
    }
    try { throw $e; }
    catch (ActionException $base) {
        if ($base !== $e) throw new RuntimeException('Exception identity changed');
    }
    $robot->wait();
}
