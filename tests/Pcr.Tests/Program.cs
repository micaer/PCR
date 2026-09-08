using Pcr.Core;

var tests = new List<(string, Action)>();
void Test(string name, Action action) => tests.Add((name, action));
void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
Scenario Basic() => new()
{
    Blocks = [new(new(0,0,0), BuildType.FLOOR), new(new(1,0,0), BuildType.FLOOR), new(new(2,0,0), BuildType.FLOOR)],
    Robots = [new("a", new(0,0,0), Heading.EAST, "unused.php")]
};
ActionResult Act(Simulation sim, ActionKind kind, string id = "a")
{
    var immediate = sim.Begin(id, kind);
    if (immediate is not null) return immediate;
    while (true)
    {
        var result = sim.Advance().FirstOrDefault(r => r.RobotId == id);
        if (result is not null) return result;
    }
}
Simulation Holding(Scenario s, BuildType type = BuildType.WALL, int units = 1)
{
    s.Materials.Add(new("held", new(1,0,1), type, units));
    var sim = new Simulation(s);
    Check(Act(sim, ActionKind.PICKUP).Success);
    return sim;
}
Test("movement consumes one tick and changes position only on completion", () => {
    var sim = new Simulation(Basic());
    Check(sim.Begin("a", ActionKind.MOVE_FORWARD) is null);
    Check(sim.Tick == 0 && sim.RobotState("a").Position == new Position(0,0,0));
    Check(sim.Advance().Single().Success && sim.Tick == 1 && sim.RobotState("a").Position == new Position(1,0,0));
});
Test("left, right, 180-degree turns and wait", () => {
    var sim = new Simulation(Basic());
    Act(sim, ActionKind.TURN_LEFT); Check(sim.RobotState("a").Direction == Heading.NORTH);
    Act(sim, ActionKind.TURN_RIGHT); Act(sim, ActionKind.TURN_RIGHT); Act(sim, ActionKind.TURN_RIGHT);
    Check(sim.RobotState("a").Direction == Heading.WEST);
    Check(Act(sim, ActionKind.WAIT).Cost == 1 && sim.Tick == 5);
});
Test("walls, materials and void prevent movement", () => {
    foreach (var obstacle in new[] {"wall", "material", "void"}) {
        var s=Basic();
        if(obstacle=="wall") s.Blocks.Add(new(new(1,0,1),BuildType.WALL));
        if(obstacle=="material") s.Materials.Add(new("m",new(1,0,1),BuildType.WALL));
        if(obstacle=="void") s.Blocks.RemoveAt(1);
        var sim=new Simulation(s); Check(!sim.Scan("a").IsMovable);
        Check(Act(sim,ActionKind.MOVE_FORWARD) is {Success:false,Cost:0,Tick:0});
    }
});
Test("one pending action per robot", () => {
    var sim = new Simulation(Basic()); sim.Begin("a",ActionKind.WAIT);
    Check(sim.Begin("a",ActionKind.TURN_LEFT)?.Error=="RobotBusy");
});
Test("configurable costs retain pending state", () => {
    var s=Basic(); s.ActionCosts[ActionKind.MOVE_FORWARD]=3; var sim=new Simulation(s);
    sim.Begin("a",ActionKind.MOVE_FORWARD); Check(sim.Advance().Length==0); Check(sim.Advance().Length==0);
    Check(sim.Advance().Single() is {Success:true,Cost:3,Tick:3});
});
Test("pickup selects top and preserves lower materials", () => {
    var s=Basic(); for(int z=1;z<=3;z++) s.Materials.Add(new($"m{z}",new(1,0,z),BuildType.WALL));
    var sim=new Simulation(s); Check(Act(sim,ActionKind.PICKUP).Success);
    Check(sim.RobotState("a").Carrying.Single().Id=="m3" && sim.Materials().Length==2);
});
Test("pickup cannot cross a floor", () => {
    var s=Basic(); s.Blocks.Add(new(new(1,0,3),BuildType.FLOOR));
    s.Materials.AddRange([new("lower",new(1,0,2),BuildType.WALL),new("upper",new(1,0,4),BuildType.WALL)]);
    var sim=new Simulation(s); Act(sim,ActionKind.PICKUP);
    Check(sim.RobotState("a").Carrying.Single().Id=="lower");
});
Test("pickup cannot cross solid wall", () => {
    var s=Basic(); s.Blocks.Add(new(new(1,0,1),BuildType.WALL)); s.Materials.Add(new("m",new(1,0,2),BuildType.WALL));
    Check(Act(new Simulation(s),ActionKind.PICKUP).Error=="NoMaterial");
});
Test("built blocks cannot be picked up", () => {
    foreach(var type in Enum.GetValues<BuildType>()) {
        var s=Basic(); s.Blocks.Add(new(new(1,0,1),type)); Check(Act(new Simulation(s),ActionKind.PICKUP).Error=="NoMaterial");
    }
});
Test("inventory respects unit capacity", () => {
    var s=Basic(); s.Materials.Add(new("m",new(1,0,1),BuildType.WALL,2));
    Check(Act(new Simulation(s),ActionKind.PICKUP).Error=="CapacityExceeded");
    s.Robots[0]=s.Robots[0] with {Capacity=2}; Check(Act(new Simulation(s),ActionKind.PICKUP).Success);
});
Test("drop stacks identical materials", () => {
    var s=Basic(); s.Materials.Add(new("lower",new(1,0,1),BuildType.WALL)); s.Materials.Add(new("upper",new(1,0,2),BuildType.WALL));
    var sim=new Simulation(s); Act(sim,ActionKind.PICKUP); Check(Act(sim,ActionKind.DROP).Success);
    Check(sim.Materials().Single(m=>m.Id=="upper").Position.Z==2);
});
Test("drop rejects mixing without consuming ticks", () => {
    var s=Basic(); s.Materials.AddRange([new("bottom",new(1,0,1),BuildType.FLOOR),new("top",new(1,0,2),BuildType.WALL)]);
    var sim=new Simulation(s); Act(sim,ActionKind.PICKUP); Check(Act(sim,ActionKind.DROP) is {Error:"MixedMaterials",Cost:0,Tick:1});
});
Test("drop cannot cross a floor and keeps inventory", () => {
    var s=Basic(); s.Blocks.Add(new(new(2,0,1),BuildType.FLOOR)); var sim=Holding(s);
    Act(sim,ActionKind.MOVE_FORWARD); Check(Act(sim,ActionKind.DROP).Error=="StackBlocked"); Check(sim.RobotState("a").Carrying.Length==1);
});
Test("drop cannot place material over void", () => {
    var s=Basic(); s.Blocks.RemoveAt(2); var sim=Holding(s); Act(sim,ActionKind.MOVE_FORWARD);
    Check(Act(sim,ActionKind.DROP).Error=="NoSupportingFloor");
});
Test("drop requires inventory", () => Check(Act(new Simulation(Basic()),ActionKind.DROP) is {Cost:0,Error:"EmptyInventory"}));
Test("build selects lowest compatible task", () => {
    var s=Basic(); s.Tasks.AddRange([
        new("pillar",new(1,0,1),BuildType.PILLAR,BuildType.PILLAR),new("low",new(1,0,2),BuildType.WALL,BuildType.WALL),new("high",new(1,0,4),BuildType.WALL,BuildType.WALL)]);
    var sim=Holding(s); Check(Act(sim,ActionKind.BUILD).Success);
    Check(sim.Tasks().Single(t=>t.Id=="low").Status=="COMPLETED" && sim.Tasks().Single(t=>t.Id=="high").Status=="PENDING");
});
Test("build below completed upper floor", () => {
    var s=Basic(); s.Blocks.Add(new(new(1,0,3),BuildType.FLOOR)); s.Tasks.Add(new("t",new(1,0,2),BuildType.WALL,BuildType.WALL));
    Check(Act(Holding(s),ActionKind.BUILD).Success);
});
Test("build cannot reach above completed or planned upper floor", () => {
    foreach(bool completed in new[]{true,false}) {
        var s=Basic();
        if(completed) s.Blocks.Add(new(new(1,0,3),BuildType.FLOOR)); else s.Tasks.Add(new("floor",new(1,0,3),BuildType.FLOOR,BuildType.FLOOR));
        s.Tasks.Add(new("t",new(1,0,4),BuildType.WALL,BuildType.WALL));
        Check(Act(Holding(s),ActionKind.BUILD) is {Error:"NoBuildableTask",Cost:0});
    }
});
Test("upper floor itself is buildable from below", () => {
    var s=Basic(); s.Tasks.Add(new("t",new(1,0,3),BuildType.FLOOR,BuildType.FLOOR));
    Check(Act(Holding(s,BuildType.FLOOR),ActionKind.BUILD).Success);
});
Test("build current-level floor over a gap", () => {
    var s=Basic(); s.Blocks.RemoveAt(2); s.Tasks.Add(new("t",new(2,0,0),BuildType.FLOOR,BuildType.FLOOR));
    var sim=Holding(s,BuildType.FLOOR); Act(sim,ActionKind.MOVE_FORWARD); Check(Act(sim,ActionKind.BUILD).Success); Check(sim.Scan("a").IsMovable);
});
Test("all five build types retain direction", () => {
    foreach(var type in Enum.GetValues<BuildType>()) {
        var s=Basic(); s.Tasks.Add(new("t",new(1,0,2),type,type,Direction:Heading.WEST));
        var sim=Holding(s,type); Check(Act(sim,ActionKind.BUILD).Success);
        Check(sim.Blocks().Single(b=>b.Position==new Position(1,0,2)).Direction==Heading.WEST);
    }
});
Test("build consumes units independently of material block", () => {
    var s=Basic(); s.Robots[0]=s.Robots[0] with {Capacity=4}; s.Tasks.Add(new("t",new(1,0,2),BuildType.WALL,BuildType.WALL,2));
    var sim=Holding(s,BuildType.WALL,4); Check(Act(sim,ActionKind.BUILD).Success); Check(sim.RobotState("a").Carrying.Single().Units==2);
});
Test("stairs ascend and descend with lower-cell scan", () => {
    var s=Basic(); s.Blocks.Add(new(new(1,0,1),BuildType.STAIRS,Heading.EAST)); s.Blocks.Add(new(new(2,0,1),BuildType.FLOOR));
    var sim=new Simulation(s); Check(sim.Scan("a").ElevationDelta==1); Act(sim,ActionKind.MOVE_FORWARD);
    Check(sim.RobotState("a").Position==new Position(1,0,1)); Check(sim.Scan("a").ElevationDelta==0);
    Act(sim,ActionKind.MOVE_FORWARD); Act(sim,ActionKind.TURN_RIGHT); Act(sim,ActionKind.TURN_RIGHT); Act(sim,ActionKind.MOVE_FORWARD);
    var scan=sim.Scan("a"); Check(scan.ElevationDelta==-1 && scan.Cells[0].Block?.Type==BuildType.FLOOR);
    Check(Act(sim,ActionKind.MOVE_FORWARD).Success && sim.RobotState("a").Position==new Position(0,0,0));
});
Test("stairs reject wrong ascent direction or blocked headroom", () => {
    var s=Basic(); s.Blocks.Add(new(new(1,0,1),BuildType.STAIRS,Heading.WEST)); Check(!new Simulation(s).Scan("a").IsMovable);
    s.Blocks[^1]=s.Blocks[^1] with {Direction=Heading.EAST}; s.Blocks.Add(new(new(1,0,2),BuildType.WALL)); Check(!new Simulation(s).Scan("a").IsMovable);
});
Test("relative scan does not turn or consume ticks", () => {
    var sim=new Simulation(Basic()); Check(sim.Scan("a","FRONT").IsMovable); Check(!sim.Scan("a","BACK").IsMovable);
    Check(sim.Tick==0 && sim.RobotState("a").Direction==Heading.EAST);
});
Test("world and scan return zone, task, material and robot snapshots", () => {
    var s=Basic(); s.Zones.Add(new("z","置き場1","MATERIAL_STORAGE",[new(1,0,0)])); s.Materials.Add(new("m",new(1,0,1),BuildType.WALL)); s.Tasks.Add(new("t",new(1,0,2),BuildType.WALL,BuildType.WALL));
    var sim=new Simulation(s); var scan=sim.Scan("a");
    Check(scan.Floor.Zone?.Name=="置き場1" && scan.Space.Materials.Length==1 && scan.Space.Tasks.Length==1);
    Check(sim.Tasks().Length==1 && sim.Zones().Length==1 && sim.Materials().Length==1 && sim.Robots().Length==1 && sim.Tick==0);
    sim.Zones()[0].Cells[0]=new(999,0,0); scan.Floor.Zone!.Cells[0]=new(999,0,0);
    Check(sim.Zones()[0].Cells[0]==new Position(1,0,0));
});
Test("simultaneous movement conflict costs one tick for loser", () => {
    var s=Basic(); s.Robots.Add(new("b",new(2,0,0),Heading.WEST,"unused")); var sim=new Simulation(s);
    Check(sim.Scan("a").IsMovable && sim.Scan("b").IsMovable);
    sim.Begin("b",ActionKind.MOVE_FORWARD); sim.Begin("a",ActionKind.MOVE_FORWARD);
    var results=sim.Advance(); Check(results[0].RobotId=="a" && results[0].Success); Check(results[1] is {Success:false,Cost:1,Tick:1});
});
Test("pickup competition never switches to next material", () => {
    var s=Basic(); s.Robots.Add(new("b",new(2,0,0),Heading.WEST,"unused")); s.Materials.AddRange([new("lower",new(1,0,1),BuildType.WALL),new("upper",new(1,0,2),BuildType.WALL)]);
    var sim=new Simulation(s); sim.Begin("b",ActionKind.PICKUP); sim.Begin("a",ActionKind.PICKUP); var results=sim.Advance();
    Check(results[0].Success && !results[1].Success && results[1].Cost==1 && sim.Materials().Single().Id=="lower");
});
Test("build competition invalidates the original task", () => {
    var s=Basic(); s.Robots.Add(new("b",new(2,0,0),Heading.WEST,"unused"));
    s.Materials.AddRange([new("lower",new(1,0,1),BuildType.WALL),new("upper",new(1,0,2),BuildType.WALL)]);
    s.Tasks.AddRange([new("low",new(1,0,1),BuildType.WALL,BuildType.WALL),new("high",new(1,0,2),BuildType.WALL,BuildType.WALL)]);
    var sim=new Simulation(s); Act(sim,ActionKind.PICKUP); Act(sim,ActionKind.PICKUP,"b");
    sim.Begin("b",ActionKind.BUILD); sim.Begin("a",ActionKind.BUILD); var results=sim.Advance();
    Check(results[0].Success && !results[1].Success && results[1].Cost==1); Check(sim.Tasks().Count(t=>t.Status=="COMPLETED")==1 && sim.RobotState("b").Carrying.Length==1);
    Check(results[1].Error=="ActionInvalidated:TaskInvalidated:TargetInvalidated");
});
Test("build blocked by dropped material does not invalidate the task", () => {
    var s=Basic(); s.Robots.Add(new("b",new(2,0,0),Heading.WEST,"unused"));
    s.Materials.AddRange([new("lower",new(1,0,1),BuildType.WALL),new("upper",new(1,0,2),BuildType.WALL)]);
    s.Tasks.Add(new("task",new(1,0,1),BuildType.WALL,BuildType.WALL));
    var sim=new Simulation(s); Act(sim,ActionKind.PICKUP); Act(sim,ActionKind.PICKUP,"b");
    Check(sim.Begin("a",ActionKind.DROP) is null && sim.Begin("b",ActionKind.BUILD) is null);
    var results=sim.Advance();
    Check(results[0].Success && results[1] is {Success:false,Cost:1,Tick:3,Error:"ActionInvalidated:NoBuildableTask"});
    Check(sim.Tasks().Single().Status=="PENDING" && sim.RobotState("b").Carrying.Length==1);
});
Test("invalid initial scenarios are rejected", () => {
    var s=Basic(); s.ActionCosts[ActionKind.WAIT]=0;
    try { _=new Simulation(s); throw new Exception("Accepted invalid cost"); } catch(ArgumentException) {}
});
Test("deterministic event and world serialization", () => {
    string Run() { var sim=new Simulation(Basic()); var results=new[]{Act(sim,ActionKind.MOVE_FORWARD),Act(sim,ActionKind.TURN_RIGHT),Act(sim,ActionKind.WAIT)}; return Json.Encode(new{results,robots=sim.Robots()}); }
    Check(Run()==Run());
});
if (args.Length == 2 && args[0] == "--integration") Integration.Register(Test, args[1]);
int failed=0;
foreach(var (name, run) in tests) {
    try { run(); Console.WriteLine("PASS " + name); }
    catch(Exception e) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + e.Message); }
}
Console.WriteLine($"{tests.Count-failed}/{tests.Count} passed");
return failed==0 ? 0 : 1;
