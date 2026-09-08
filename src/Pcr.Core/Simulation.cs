namespace Pcr.Core;

public sealed class Simulation
{
    private sealed class Robot(RobotSpec spec)
    {
        public string Id = spec.Id;
        public Position Position = spec.Position;
        public Heading Direction = spec.Direction;
        public int Capacity = spec.Capacity;
        public List<Material> Carrying = [];
    }
    private sealed record Plan(string RobotId, ActionKind Action, long Due, int Cost, Position? Target = null, string? Entity = null);
    private readonly Dictionary<Position, Block> blocks;
    private readonly Dictionary<string, Material> materials;
    private readonly Dictionary<string, BuildTask> tasks;
    private readonly Dictionary<string, Robot> robots;
    private readonly Zone[] zones;
    private readonly Dictionary<string, Plan> pending = new(StringComparer.Ordinal);
    private readonly Dictionary<ActionKind, int> costs;
    private readonly int height;
    public long Tick { get; private set; }
    public bool HasPending => pending.Count > 0;

    public Simulation(Scenario scenario)
    {
        height = scenario.MaxHeight;
        if (height < 1 || scenario.ActionCosts.Values.Any(v => v < 1)) throw new ArgumentException("Invalid height or action cost");
        blocks = scenario.Blocks.ToDictionary(x => x.Position);
        materials = scenario.Materials.ToDictionary(x => x.Id, StringComparer.Ordinal);
        tasks = scenario.Tasks.ToDictionary(x => x.Id, StringComparer.Ordinal);
        robots = scenario.Robots.ToDictionary(x => x.Id, x => new Robot(x), StringComparer.Ordinal);
        zones = scenario.Zones.Select(z => z with { Cells = z.Cells.ToArray() }).ToArray();
        costs = new(scenario.ActionCosts);
        if (materials.Values.Any(m => m.Units < 1) || tasks.Values.Any(t => t.RequiredUnits < 1 || t.Status != "PENDING") || robots.Values.Any(r => r.Capacity < 1))
            throw new ArgumentException("Invalid units, capacity or initial task status");
        if (materials.Values.Select(m => m.Position).Distinct().Count() != materials.Count || tasks.Values.Select(t => t.Position).Distinct().Count() != tasks.Count)
            throw new ArgumentException("Duplicate occupied cell");
        if (materials.Values.Any(m => blocks.ContainsKey(m.Position)) || tasks.Values.Any(t => blocks.ContainsKey(t.Position))) throw new ArgumentException("Overlapping block");
        if (robots.Values.Select(r => r.Position).Distinct().Count() != robots.Count || robots.Values.Any(r => !Supported(r.Position) || !Clear(r.Position.Up(1), r.Id)))
            throw new ArgumentException("Robot requires an unoccupied supported floor");
        if (zones.Select(z => z.Id).Distinct().Count() != zones.Length || zones.SelectMany(z => z.Cells).Distinct().Count() != zones.Sum(z => z.Cells.Length)
            || zones.Any(z => z.Type != "MATERIAL_STORAGE" || z.Cells.Any(p => !blocks.TryGetValue(p, out var b) || b.Type != BuildType.FLOOR)))
            throw new ArgumentException("Zones must have unique floor cells and IDs");
    }

    private Robot Get(string id) => robots.TryGetValue(id, out var r) ? r : throw new RuleException("UnknownRobot");
    private RobotInfo Info(Robot r) => new(r.Id, r.Position, r.Direction, pending.ContainsKey(r.Id) ? "BUSY" : "IDLE", r.Carrying.ToArray(), r.Capacity);
    public RobotInfo RobotState(string id) => Info(Get(id));
    public RobotInfo[] Robots() => robots.Values.OrderBy(r => r.Id, StringComparer.Ordinal).Select(Info).ToArray();
    public Material[] Materials() => materials.Values.OrderBy(m => m.Id, StringComparer.Ordinal).ToArray();
    public BuildTask[] Tasks() => tasks.Values.OrderBy(t => t.Id, StringComparer.Ordinal).ToArray();
    public Zone[] Zones() => zones.Select(z => z with { Cells = z.Cells.ToArray() }).ToArray();
    public Block[] Blocks() => blocks.Values.OrderBy(b => b.Position.Z).ThenBy(b => b.Position.Y).ThenBy(b => b.Position.X).ToArray();
    private bool Supported(Position p) => blocks.TryGetValue(p, out var b) && b.Type is BuildType.FLOOR or BuildType.STAIRS;
    private bool Clear(Position body, string id) => !blocks.ContainsKey(body) && !materials.Values.Any(m => m.Position == body)
        && !robots.Values.Any(r => r.Id != id && r.Position.Up(1) == body);
    private Position? Destination(Robot r, Heading h)
    {
        var p = r.Position.Ahead(h);
        Position? destination = null;
        // A stair occupies the cell above its approach floor and faces uphill.
        if (blocks.TryGetValue(p.Up(1), out var up) && up.Type == BuildType.STAIRS && up.Direction == h)
            destination = p.Up(1);
        else if (blocks.TryGetValue(r.Position, out var down) && down.Type == BuildType.STAIRS && (int)h == ((int)down.Direction + 2) % 4 && Supported(p.Up(-1)))
            destination = p.Up(-1);
        else if (Supported(p)) destination = p;
        // The top of a stair is represented by a standing position at stair height.
        if (destination is { } d && Clear(d.Up(1), r.Id) && !robots.Values.Any(o => o.Id != r.Id && o.Position == d)) return d;
        return null;
    }
    public ScanInfo Scan(string id, string relative = "FRONT")
    {
        var r = Get(id);
        int offset = relative switch { "FRONT" => 0, "RIGHT" => 1, "BACK" => 2, "LEFT" => 3, _ => throw new RuleException("InvalidDirection") };
        var h = (Heading)(((int)r.Direction + offset) % 4);
        var front = r.Position.Ahead(h);
        var cells = Enumerable.Range(-1, height + 2).Select(n =>
        {
            var p = front.Up(n);
            var zone = zones.FirstOrDefault(z => z.Cells.Contains(p));
            return new CellInfo(n, p, blocks.GetValueOrDefault(p), tasks.Values.FirstOrDefault(t => t.Position == p && t.Status == "PENDING"),
                zone is null ? null : zone with { Cells = zone.Cells.ToArray() }, materials.Values.FirstOrDefault(m => m.Position == p),
                robots.Values.Where(o => o.Position.Up(1) == p).Select(Info).FirstOrDefault());
        }).ToArray();
        var space = cells.Where(c => c.Offset > 0).ToArray();
        var d = Destination(r, h);
        return new(d is not null, d, d?.Z - r.Position.Z, cells[1], new(space.Select(c => c.Robot).FirstOrDefault(x => x is not null),
            space.Where(c => c.Material is not null).Select(c => c.Material!).ToArray(), space.Where(c => c.Task is not null).Select(c => c.Task!).ToArray()), cells);
    }
    private int Ceiling(Position front)
    {
        for (int i = 1; i <= height; i++)
        {
            var p = front.Up(i);
            if (blocks.TryGetValue(p, out var b) && b.Type == BuildType.FLOOR || tasks.Values.Any(t => t.Position == p && t.Type == BuildType.FLOOR && t.Status == "PENDING")) return i;
        }
        return height;
    }
    private List<Position> Reachable(Position front)
    {
        var result = new List<Position>();
        for (int i = 1; i <= height; i++)
        {
            var p = front.Up(i);
            if (blocks.ContainsKey(p) || robots.Values.Any(r => r.Position.Up(1) == p)) break;
            result.Add(p);
        }
        return result;
    }
    private Plan Prepare(string id, ActionKind kind)
    {
        var r = Get(id);
        var front = r.Position.Ahead(r.Direction);
        int cost = costs.GetValueOrDefault(kind, 1);
        var plan = new Plan(id, kind, Tick + cost, cost);
        switch (kind)
        {
            case ActionKind.MOVE_FORWARD: return plan with { Target = Destination(r, r.Direction) ?? throw new RuleException("MovementBlocked") };
            case ActionKind.PICKUP:
                var range = Reachable(front);
                var m = materials.Values.Where(m => range.Contains(m.Position)).OrderByDescending(m => m.Position.Z).FirstOrDefault() ?? throw new RuleException("NoMaterial");
                if (r.Carrying.Sum(x => x.Units) + m.Units > r.Capacity) throw new RuleException("CapacityExceeded");
                return plan with { Target = m.Position, Entity = m.Id };
            case ActionKind.DROP:
                var held = r.Carrying.FirstOrDefault() ?? throw new RuleException("EmptyInventory");
                var reach = Reachable(front);
                if (!Supported(front)) throw new RuleException("NoSupportingFloor");
                var pile = materials.Values.Where(m => reach.Contains(m.Position)).ToArray();
                if (pile.Any(m => m.Type != held.Type)) throw new RuleException("MixedMaterials");
                var target = front.Up(pile.Length == 0 ? 1 : pile.Max(m => m.Position.Z) - front.Z + 1);
                if (!reach.Contains(target)) throw new RuleException("StackBlocked");
                return plan with { Target = target, Entity = held.Id };
            case ActionKind.BUILD:
                var limit = Ceiling(front);
                var task = tasks.Values.Where(t => t.Status == "PENDING" && t.Position.X == front.X && t.Position.Y == front.Y
                    && t.Position.Z >= front.Z && t.Position.Z <= front.Z + limit
                    && r.Carrying.Where(m => m.Type == t.RequiredMaterial).Sum(m => m.Units) >= t.RequiredUnits
                    && !blocks.ContainsKey(t.Position) && !materials.Values.Any(m => m.Position == t.Position)
                    && !robots.Values.Any(o => o.Position.Up(1) == t.Position || o.Position == t.Position))
                    .OrderBy(t => t.Position.Z).ThenBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault() ?? throw new RuleException("NoBuildableTask");
                return plan with { Target = task.Position, Entity = task.Id };
            case ActionKind.TURN_LEFT: case ActionKind.TURN_RIGHT: case ActionKind.WAIT: return plan;
            default: throw new RuleException("InvalidAction");
        }
    }
    public ActionResult? Begin(string id, ActionKind kind)
    {
        if (pending.ContainsKey(id)) return new(id, kind, false, "RobotBusy", Tick, 0);
        try { pending.Add(id, Prepare(id, kind)); return null; }
        catch (RuleException e) { return new(id, kind, false, e.Message, Tick, 0); }
    }
    public ActionResult[] Advance()
    {
        if (!HasPending) return [];
        Tick++;
        var results = new List<ActionResult>();
        foreach (var plan in pending.Values.Where(p => p.Due <= Tick).OrderBy(p => p.RobotId, StringComparer.Ordinal).ToArray())
        {
            pending.Remove(plan.RobotId);
            try
            {
                var fresh = Prepare(plan.RobotId, plan.Action);
                if (fresh.Target != plan.Target || fresh.Entity != plan.Entity) throw new RuleException("TargetInvalidated");
                var r = Get(plan.RobotId);
                switch (plan.Action)
                {
                    case ActionKind.MOVE_FORWARD: r.Position = plan.Target!.Value; break;
                    case ActionKind.TURN_LEFT: r.Direction = (Heading)(((int)r.Direction + 3) % 4); break;
                    case ActionKind.TURN_RIGHT: r.Direction = (Heading)(((int)r.Direction + 1) % 4); break;
                    case ActionKind.PICKUP:
                        var m = materials[plan.Entity!]; materials.Remove(m.Id); r.Carrying.Add(m); break;
                    case ActionKind.DROP:
                        var held = r.Carrying.First(x => x.Id == plan.Entity); r.Carrying.Remove(held);
                        materials.Add(held.Id, held with { Position = plan.Target!.Value }); break;
                    case ActionKind.BUILD:
                        var t = tasks[plan.Entity!];
                        int remaining = t.RequiredUnits;
                        foreach (var item in r.Carrying.Where(m => m.Type == t.RequiredMaterial).ToArray())
                        {
                            int used = Math.Min(remaining, item.Units); remaining -= used;
                            r.Carrying.Remove(item);
                            if (item.Units > used) r.Carrying.Add(item with { Units = item.Units - used });
                            if (remaining == 0) break;
                        }
                        blocks.Add(t.Position, new(t.Position, t.Type, t.Direction)); tasks[t.Id] = t with { Status = "COMPLETED" }; break;
                }
                results.Add(new(r.Id, plan.Action, true, null, Tick, plan.Cost, plan.Entity, plan.Target));
            }
            catch (RuleException e)
            {
                // A blocked build is not necessarily an invalidated task. Inspect the original target.
                bool taskInvalidated = plan.Action == ActionKind.BUILD && plan.Entity is not null
                    && (!tasks.TryGetValue(plan.Entity, out var task) || task.Status != "PENDING");
                string error = "ActionInvalidated:" + (taskInvalidated ? "TaskInvalidated:" : "") + e.Message;
                results.Add(new(plan.RobotId, plan.Action, false, error, Tick, plan.Cost, plan.Entity, plan.Target));
            }
        }
        return results.ToArray();
    }
}
