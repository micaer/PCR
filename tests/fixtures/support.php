<?php
declare(strict_types=1);
namespace Fixture;
enum Outcome { case READY; }
interface Contract { public function value(): Outcome; }
trait Value { public function value(): Outcome { return Outcome::READY; } }
class Base implements Contract { use Value; }
class Program extends Base {}
