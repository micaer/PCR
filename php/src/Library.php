<?php
declare(strict_types=1);
namespace PCR;

class Navigation {
    /** @var array<string, array<string, array{Position, Heading}>> */
    protected array $graph = [];
    protected array $explored = [];
    public function __construct(protected Robot $robot) {}
    public function turnTo(Heading $heading): void {
        $headings = Heading::cases();
        $current = array_search($this->robot->direction(), $headings, true);
        $target = array_search($heading, $headings, true);
        $delta = ($target - $current + 4) % 4;
        if ($delta === 3) $this->robot->turn(TurnDirection::LEFT);
        else for ($i=0; $i<$delta; $i++) $this->robot->turn(TurnDirection::RIGHT);
    }
    protected function observe(): void {
        $p = $this->robot->position();
        $h = array_search($this->robot->direction(), Heading::cases(), true);
        $this->graph[$p->key()] = [];
        foreach (Direction::cases() as $i=>$relative) {
            $scan = $this->robot->scan($relative);
            if ($scan->isMovable()) {
                $d = $scan->destination();
                $this->graph[$p->key()][$d->key()] = [$d, Heading::cases()[($h+$i)%4]];
            }
        }
        $this->explored[$p->key()] = true;
    }
    /** BFS over observed local scans, falling back to an unexplored reachable cell.
     * @return array<array{Position, Heading}> */
    protected function findPath(Position $target): array {
        $start = $this->robot->position()->key();
        $queue = [[$start, []]]; $seen = [$start=>true]; $frontier = null;
        for ($i=0; $i<count($queue); $i++) {
            [$key, $path] = $queue[$i];
            if ($key === $target->key()) return $path;
            if (!isset($this->explored[$key]) && $frontier === null) $frontier = $path;
            foreach ($this->graph[$key] ?? [] as $next=>[$position, $heading]) {
                if (isset($seen[$next])) continue;
                $seen[$next] = true;
                $queue[] = [$next, [...$path, [$position, $heading]]];
            }
        }
        return $frontier ?? throw new NavigationException('No reachable route to '.$target->key());
    }
    public function moveTo(Position $target): void {
        // Refresh cached topology for each trip; no privileged world geometry API.
        $this->graph = []; $this->explored = [];
        for ($steps=0; $steps<4096; $steps++) {
            if ($this->robot->position() == $target) return;
            $this->observe();
            $path = $this->findPath($target);
            if (!$path) throw new NavigationException('Empty route');
            [$next, $heading] = $path[0];
            $this->turnTo($heading);
            $scan = $this->robot->scan();
            if (!$scan->isMovable() || $scan->destination() != $next) continue;
            try { $this->robot->moveForward(); }
            catch (ActionException $e) { $this->robot->wait(); }
        }
        throw new NavigationException('Navigation step limit exceeded');
    }
    public function moveAdjacent(Position $target, ?int $floorZ = null): void {
        $z = $floorZ ?? $this->robot->position()->z;
        $approaches = [
            [new Position($target->x-1,$target->y,$z),Heading::EAST],
            [new Position($target->x+1,$target->y,$z),Heading::WEST],
            [new Position($target->x,$target->y-1,$z),Heading::SOUTH],
            [new Position($target->x,$target->y+1,$z),Heading::NORTH]
        ];
        $origin = $this->robot->position();
        usort($approaches, static fn($a,$b)=>$a[0]->distance($origin)<=>$b[0]->distance($origin));
        foreach ($approaches as [$p,$h]) {
            try { $this->moveTo($p); $this->turnTo($h); return; }
            catch (NavigationException $e) { /* Try another side. */ }
        }
        throw new NavigationException('No reachable approach');
    }
}
class TaskManager {
    public function __construct(protected World $world, protected Robot $robot) {}
    public function findNearest(?BuildType $type = null): ?TaskInfo {
        $tasks = array_values(array_filter($this->world->tasks(), static fn($t)=>$t->status()==='PENDING' && ($type===null || $t->type()===$type)));
        $p=$this->robot->position();
        usort($tasks, static fn($a,$b)=>($a->position()->distance($p)<=>$b->position()->distance($p)) ?: strcmp($a->id(),$b->id()));
        return $tasks[0] ?? null;
    }
}
class MaterialManager {
    public function __construct(protected World $world, protected Robot $robot) {}
    public function findNearest(MaterialType $type): ?MaterialInfo {
        $items=array_values(array_filter($this->world->materials(), static fn($m)=>$m->type()===$type));
        $p=$this->robot->position();
        usort($items, static fn($a,$b)=>($a->position()->distance($p)<=>$b->position()->distance($p)) ?: strcmp($a->id(),$b->id()));
        return $items[0] ?? null;
    }
}
class ZoneManager {
    public function __construct(protected World $world) {}
    public function findByName(string $name): ?ZoneInfo {
        foreach ($this->world->zones() as $zone) if ($zone->name()===$name) return $zone;
        return null;
    }
}
class StorageManager {
    public function __construct(protected Robot $robot, protected Navigation $navigation) {}
    public function store(ZoneInfo $zone): void {
        foreach ($zone->cells() as $cell) {
            try { $this->navigation->moveAdjacent($cell, $cell->z); $this->robot->drop(); return; }
            catch (ActionException|NavigationException $e) { /* Try the next storage cell. */ }
        }
        throw new NavigationException('Storage has no accessible free stack');
    }
}
class Construction {
    public function __construct(protected Robot $robot, protected Navigation $navigation) {}
    // build() always uses the engine's lowest compatible task rule.
    public function buildAt(TaskInfo $task, ?int $floorZ = null): void {
        $this->navigation->moveAdjacent($task->position(), $floorZ);
        $this->robot->build();
    }
}
interface RobotProgram { public function run(Robot $robot, World $world): void; }
