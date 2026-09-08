<?php
declare(strict_types=1);
use PCR\{BuildType, MaterialType, TaskManager, MaterialManager, ZoneManager, ZoneType, World, TaskInfo};
$tasks = new TaskManager($world, $robot);
$materials = new MaterialManager($world, $robot);
$zones = new ZoneManager($world);
for ($i=0; $i<3; $i++) {
    if ($tasks->findNearest()?->id() !== 'a-task' || $tasks->findNearest(BuildType::WALL)?->id() !== 'a-task') throw new RuntimeException('Task tie/filter failed');
    if ($materials->findNearest(MaterialType::WALL)?->id() !== 'a-material') throw new RuntimeException('Material tie/filter failed');
}
if ($tasks->findNearest(BuildType::STAIRS) !== null || $materials->findNearest(MaterialType::STAIRS) !== null) throw new RuntimeException('Expected no match');
if ($zones->findByName('置き場1')?->id() !== 'z-first' || $zones->findByType(ZoneType::MATERIAL_STORAGE)?->id() !== 'a-zone') throw new RuntimeException('Zone lookup failed');
if ($zones->findByName('missing') !== null) throw new RuntimeException('Unexpected zone');
$robot->pickup();
if ($materials->findNearest(MaterialType::WALL)?->id() !== 'b-material') throw new RuntimeException('Carried material returned by search');
$robot->build();
if ($tasks->findNearest()?->id() !== 'b-task') throw new RuntimeException('Completed task returned by search');
// The current Core cannot cancel tasks; exercise the library filter with read-only DTOs.
$inactive = new class extends World {
    public function __construct() {}
    public function tasks(): array {
        return [new TaskInfo(['id'=>'cancelled','status'=>'CANCELLED']), new TaskInfo(['id'=>'done','status'=>'COMPLETED'])];
    }
};
if ((new TaskManager($inactive, $robot))->findNearest() !== null) throw new RuntimeException('Inactive task returned');
