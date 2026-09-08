<?php
declare(strict_types=1);
// An independent Fiber/process acts concurrently, one primitive per tick.
for ($i=0; $i<8; $i++) {
    if (count($world->robots()) !== 2) throw new RuntimeException('Expected two robots');
    $robot->wait();
}
