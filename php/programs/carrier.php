<?php
declare(strict_types=1);
use PCR\{MaterialType, MaterialManager, Navigation, ZoneManager, StorageManager};

// Carry one PILLAR block to storage. No task reservation or robot coordination is needed.
$navigation = new Navigation($robot);
$materials = new MaterialManager($world, $robot);
$zone = (new ZoneManager($world))->findByName('置き場1') ?? throw new RuntimeException('Missing storage zone');
$material = $materials->findNearest(MaterialType::PILLAR) ?? throw new RuntimeException('No PILLAR material');
$navigation->moveAdjacent($material->position(), 0);
$robot->pickup();
(new StorageManager($robot, $navigation))->store($zone);
