<?php
declare(strict_types=1);
namespace PCR;

enum TurnDirection { case LEFT; case RIGHT; }
enum Direction { case FRONT; case RIGHT; case BACK; case LEFT; }
enum Heading: string { case NORTH='NORTH'; case EAST='EAST'; case SOUTH='SOUTH'; case WEST='WEST'; }
enum BuildType: string { case FLOOR='FLOOR'; case WALL='WALL'; case PILLAR='PILLAR'; case WINDOW='WINDOW'; case STAIRS='STAIRS'; }
enum MaterialType: string { case FLOOR='FLOOR'; case WALL='WALL'; case PILLAR='PILLAR'; case WINDOW='WINDOW'; case STAIRS='STAIRS'; }
enum ZoneType: string { case MATERIAL_STORAGE='MATERIAL_STORAGE'; }

class ActionException extends \RuntimeException {
    public function __construct(string $message, public readonly int $tick, public readonly int $cost) { parent::__construct($message); }
}
class ActionInvalidatedException extends ActionException {}
class TaskInvalidatedException extends ActionInvalidatedException {}
class NavigationException extends \RuntimeException {}

final class Transport {
    public function write(array $message): void { fwrite(STDOUT, json_encode($message, JSON_THROW_ON_ERROR) . "\n"); fflush(STDOUT); }
    public function exchange(array $message): array {
        $this->write($message);
        $line = fgets(STDIN);
        if ($line === false) throw new \RuntimeException('Simulator disconnected');
        return json_decode($line, true, flags: JSON_THROW_ON_ERROR);
    }
    public function query(string $query, array $extra = []): array {
        $result = $this->exchange(['op'=>'query', 'query'=>$query] + $extra);
        if (!$result['ok']) throw new \RuntimeException($result['error']);
        return $result['data'];
    }
}
final readonly class Position {
    public function __construct(public int $x, public int $y, public int $z) {}
    public static function from(array $p): self { return new self($p['x'], $p['y'], $p['z']); }
    public function key(): string { return "$this->x,$this->y,$this->z"; }
    public function distance(self $other): int { return abs($this->x-$other->x)+abs($this->y-$other->y)+abs($this->z-$other->z); }
}
class View {
    public function __construct(protected readonly array $data) {}
    public function id(): string { return $this->data['id']; }
    public function position(): Position { return Position::from($this->data['position']); }
}
class TaskInfo extends View {
    public function type(): BuildType { return BuildType::from($this->data['type']); }
    public function status(): string { return $this->data['status']; }
    public function requiredMaterial(): MaterialType { return MaterialType::from($this->data['requiredMaterial']); }
    public function requiredUnits(): int { return $this->data['requiredUnits']; }
}
class MaterialInfo extends View {
    public function type(): MaterialType { return MaterialType::from($this->data['type']); }
    public function units(): int { return $this->data['units']; }
}
class ZoneInfo extends View {
    public function name(): string { return $this->data['name']; }
    public function type(): string { return $this->data['type']; }
    /** @return Position[] */
    public function cells(): array { return array_map(Position::from(...), $this->data['cells']); }
}
class RobotInfo extends View {
    public function direction(): Heading { return Heading::from($this->data['direction']); }
    public function status(): string { return $this->data['status']; }
}
class BlockInfo extends View {
    public function type(): BuildType { return BuildType::from($this->data['type']); }
    public function direction(): Heading { return Heading::from($this->data['direction']); }
}
class CellInfo extends View {
    public function block(): ?BlockInfo { return isset($this->data['block']) ? new BlockInfo($this->data['block']) : null; }
    public function task(): ?TaskInfo { return isset($this->data['task']) ? new TaskInfo($this->data['task']) : null; }
    public function zone(): ?ZoneInfo { return isset($this->data['zone']) ? new ZoneInfo($this->data['zone']) : null; }
    public function material(): ?MaterialInfo { return isset($this->data['material']) ? new MaterialInfo($this->data['material']) : null; }
    public function robot(): ?RobotInfo { return isset($this->data['robot']) ? new RobotInfo($this->data['robot']) : null; }
}
class SpaceInfo extends View {
    /** Existing scan snapshot, ordered from the floor upward. @return CellInfo[] */
    public function cells(): array { return array_map(static fn($c)=>new CellInfo($c), $this->data['cells']); }
    public function robot(): ?RobotInfo { return isset($this->data['robot']) ? new RobotInfo($this->data['robot']) : null; }
    /** @return MaterialInfo[] */
    public function materials(): array { return array_map(static fn($m)=>new MaterialInfo($m), $this->data['materials']); }
    /** @return TaskInfo[] */
    public function tasks(): array { return array_map(static fn($t)=>new TaskInfo($t), $this->data['tasks']); }
}
class ScanInfo extends View {
    public function isMovable(): bool { return $this->data['isMovable']; }
    public function destination(): ?Position { return isset($this->data['destination']) ? Position::from($this->data['destination']) : null; }
    public function elevationDelta(): ?int { return $this->data['elevationDelta']; }
    public function floor(): CellInfo { return new CellInfo($this->data['floor']); }
    public function space(): SpaceInfo {
        return new SpaceInfo($this->data['space'] + ['cells'=>array_values(array_filter(
            $this->data['cells'], static fn($c)=>$c['offset'] > 0
        ))]);
    }
}
class Robot {
    public function __construct(private readonly Transport $wire) {}
    private function state(): array { return $this->wire->query('robot'); }
    public function position(): Position { return Position::from($this->state()['position']); }
    public function direction(): Heading { return Heading::from($this->state()['direction']); }
    public function carrying(): ?MaterialInfo { $items=$this->state()['carrying']; return $items ? new MaterialInfo($items[0]) : null; }
    public function scan(Direction $direction = Direction::FRONT): ScanInfo { return new ScanInfo($this->wire->query('scan', ['direction'=>$direction->name])); }
    private function action(string $name): void {
        $result = \Fiber::suspend(['op'=>'action', 'action'=>$name]);
        if (!$result['ok']) {
            $class = match (true) {
                str_starts_with($result['error'], 'ActionInvalidated:TaskInvalidated:') => TaskInvalidatedException::class,
                str_starts_with($result['error'], 'ActionInvalidated:') => ActionInvalidatedException::class,
                default => ActionException::class,
            };
            throw new $class($result['error'], $result['tick'], $result['cost']);
        }
    }
    public function moveForward(): void { $this->action('MOVE_FORWARD'); }
    public function turn(TurnDirection $d): void { $this->action('TURN_'.$d->name); }
    public function pickup(): void { $this->action('PICKUP'); }
    public function drop(): void { $this->action('DROP'); }
    public function build(): void { $this->action('BUILD'); }
    public function wait(): void { $this->action('WAIT'); }
}
class World {
    public function __construct(private readonly Transport $wire) {}
    /** @return TaskInfo[] */
    public function tasks(): array { return array_map(static fn($v)=>new TaskInfo($v), $this->wire->query('tasks')); }
    /** @return ZoneInfo[] */
    public function zones(): array { return array_map(static fn($v)=>new ZoneInfo($v), $this->wire->query('zones')); }
    /** @return MaterialInfo[] */
    public function materials(): array { return array_map(static fn($v)=>new MaterialInfo($v), $this->wire->query('materials')); }
    /** @return RobotInfo[] */
    public function robots(): array { return array_map(static fn($v)=>new RobotInfo($v), $this->wire->query('robots')); }
}
