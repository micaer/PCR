<?php
declare(strict_types=1);
use PCR\{Navigation, ZoneManager, StorageManager};
$robot->pickup();
$storage = new StorageManager($robot, new Navigation($robot));
$zone = (new ZoneManager($world))->findByName('置き場1');
$candidate = $storage->findDropPosition($zone);
if ($candidate != $expected) throw new RuntimeException('Wrong storage candidate: '.($candidate?->key() ?? 'null'));
$storage->store($zone);
if ($robot->carrying() !== null) throw new RuntimeException('Storage did not drop');
$delivered = array_values(array_filter($world->materials(), static fn($m)=>$m->id()==='held'))[0];
if ($delivered->position() != $destination) throw new RuntimeException('Wrong drop destination');
