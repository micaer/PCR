<?php
declare(strict_types=1);
// Prepare both inventories, then exercise the actual sample's competing build recovery.
if ($robot->position()->x === 0) {
    $robot->pickup();
    $robot->wait();
} else {
    $robot->wait();
    $robot->pickup();
}
require __DIR__.'/../../php/programs/builder.php';
