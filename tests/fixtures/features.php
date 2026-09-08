<?php
declare(strict_types=1);
require __DIR__ . '/support.php';
use PCR\{ActionException, Navigation, Heading, Position};
use Fixture\{Program, Outcome};

$program = new Program();
if ($program->value() !== Outcome::READY) throw new RuntimeException('PHP class/trait/interface/enum failed');
try {
    $robot->pickup();
    throw new RuntimeException('Expected immediate failure');
} catch (ActionException $e) {
    if ($e->tick !== 0 || $e->cost !== 0) throw new RuntimeException('Immediate failure consumed a tick');
}
$navigation = new class($robot) extends Navigation {
    protected function findPath(Position $target): array { return parent::findPath($target); }
};
$navigation->turnTo(Heading::EAST);
$navigation->moveTo(new Position(1, 0, 0));
$robot->wait();
if ($robot->position() != new Position(1,0,0)) throw new RuntimeException('Fiber did not resume');
echo "user output is isolated\n";
