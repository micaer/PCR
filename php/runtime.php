<?php
declare(strict_types=1);
require __DIR__ . '/src/Api.php';
require __DIR__ . '/src/Library.php';

use PCR\{Transport, Robot, World};

// stdout is reserved for JSONL; ordinary user output goes to stderr.
ob_start(static function (string $text): string { fwrite(STDERR, $text); return ''; }, 1);
$wire = new Transport();
try {
    mt_srand((int)($argv[2] ?? 0));
    $robot = new Robot($wire);
    $world = new World($wire);
    $program = $argv[1] ?? throw new RuntimeException('Missing program');
    $fiber = new Fiber(static function () use ($program, $robot, $world): void { require $program; });
    $action = $fiber->start();
    while (!$fiber->isTerminated()) {
        $response = $wire->exchange($action);
        $action = $fiber->resume($response);
    }
    $wire->write(['op' => 'done']);
} catch (Throwable $e) {
    $wire->write(['op' => 'error', 'message' => get_class($e) . ': ' . $e->getMessage()]);
    exit(1);
}
