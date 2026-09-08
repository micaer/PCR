<?php
// The process belongs to robot a; supplying b must not change that ownership.
fwrite(STDOUT, json_encode(['op'=>'action','action'=>'MOVE_FORWARD','robotId'=>'b']) . "\n");
$result = json_decode(fgets(STDIN), true, flags:JSON_THROW_ON_ERROR);
if (!$result['ok']) throw new RuntimeException('Unexpected failure');
