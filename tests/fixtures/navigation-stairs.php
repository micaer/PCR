<?php
declare(strict_types=1);
use PCR\{Navigation, Position};
$navigation = new Navigation($robot);
$navigation->moveTo(new Position(2,0,1));
if ($robot->position() != new Position(2,0,1)) throw new RuntimeException('Did not reach upstairs');
$navigation->moveTo(new Position(0,0,0));
if ($robot->position() != new Position(0,0,0)) throw new RuntimeException('Did not return downstairs');
