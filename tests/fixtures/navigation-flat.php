<?php
declare(strict_types=1);
use PCR\{Navigation, NavigationException, Position, Heading};
$navigation = new class($robot) extends Navigation {
    public int $searches = 0;
    protected function findPath(Position $target): array {
        $this->searches++;
        return parent::findPath($target);
    }
};
$navigation->turnTo(Heading::NORTH); // LEFT from EAST
$navigation->turnTo(Heading::EAST);  // RIGHT from NORTH
$navigation->moveTo(new Position(2,0,0));
if ($robot->position() != new Position(2,0,0) || $navigation->searches === 0) throw new RuntimeException('Navigation failed');
try {
    $navigation->moveTo(new Position(1,0,0)); // Wall cell; must never be entered.
    throw new RuntimeException('Entered blocked cell');
} catch (NavigationException $e) {}
$navigation->moveTo(new Position(2,0,0));
