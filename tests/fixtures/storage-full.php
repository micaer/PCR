<?php
use PCR\{Navigation, NavigationException, ZoneManager, StorageManager};
$robot->pickup();
$storage = new StorageManager($robot, new Navigation($robot));
$zone = (new ZoneManager($world))->findByName('置き場1');
if ($storage->findDropPosition($zone) !== null) throw new RuntimeException('Blocked zone returned a candidate');
try {
    $storage->store($zone);
    throw new RuntimeException('Stored in a blocked zone');
} catch (NavigationException $e) {}
if ($robot->carrying() === null) throw new RuntimeException('Lost material');
