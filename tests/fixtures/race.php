<?php
declare(strict_types=1);
use PCR\{ActionException, ActionInvalidatedException};
try {
    $robot->pickup();
    if ($robot->carrying() === null) throw new RuntimeException('Missing inventory');
} catch (ActionInvalidatedException $e) {
    if (get_class($e) !== ActionInvalidatedException::class) throw new RuntimeException('Non-task conflict misclassified');
    if ($e->cost !== 1 || $e->tick !== 1) throw new RuntimeException('Wrong conflict cost');
    try { throw $e; }
    catch (ActionException $base) {
        if ($base !== $e) throw new RuntimeException('Exception identity changed');
    }
    $robot->wait();
}
