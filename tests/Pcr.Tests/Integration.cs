using System.Diagnostics;
using Pcr.Core;

static class Integration
{
    public static void Register(Action<string, Action> test, string root)
    {
        root = Path.GetFullPath(root);
        var directory = Path.Combine(root,"artifacts","integration");
        Directory.CreateDirectory(directory);
        string Fixture(string name) => Path.Combine(root,"tests","fixtures",name+".php");
        void Check(bool value, string message) { if(!value) throw new Exception(message); }
        Scenario Basic(string program) => new()
        {
            MaxTicks=100,
            Blocks=[new(new(0,0,0),BuildType.FLOOR),new(new(1,0,0),BuildType.FLOOR),new(new(2,0,0),BuildType.FLOOR)],
            Robots=[new("a",new(0,0,0),Heading.EAST,Fixture(program))]
        };
        (int Code,string Text,string Result) Run(string name, Scenario? scenario = null)
        {
            string file = scenario is null ? Path.Combine(root,"scenarios","demo.json") : Path.Combine(directory,name+".json");
            if(scenario is not null) File.WriteAllText(file,Json.Encode(scenario));
            string output=Path.Combine(directory,name+"-result.json");
            var start=new ProcessStartInfo("dotnet") {WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,UseShellExecute=false};
            foreach(var arg in new[]{Path.Combine(root,"src","Pcr.Simulator","bin","Debug","net9.0","Pcr.Simulator.dll"),file,"--output",output}) start.ArgumentList.Add(arg);
            using var process=Process.Start(start)!;
            var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(20000)) {process.Kill(true); throw new Exception("Integration process timed out");}
            return (process.ExitCode,stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult(),process.ExitCode==0 ? File.ReadAllText(output) : "");
        }
        test("PHP demo completes and repeats byte-identically",()=>{
            var first=Run("demo-1"); var second=Run("demo-2");
            Check(first.Code==0 && second.Code==0,first.Text+second.Text);
            Check(first.Result==second.Result,"Non-deterministic results");
            using var result=System.Text.Json.JsonDocument.Parse(first.Result);
            Check(result.RootElement.GetProperty("tasks").EnumerateArray().All(t=>t.GetProperty("status").GetString()=="COMPLETED"),"Incomplete tasks");
            Check(result.RootElement.GetProperty("robots")[0].GetProperty("position").GetProperty("z").GetInt32()==0,"Demo did not return downstairs");
        });
        test("PHP features, subclassed navigation, Fiber resume and zero-tick exception",()=>{
            var result=Run("features",Basic("features")); Check(result.Code==0,result.Text);
            Check(result.Text.Contains("tick=2") && result.Text.Contains("user output is isolated"),result.Text);
        });
        test("PHP pickup conflict throws ActionInvalidatedException catchable as ActionException",()=>{
            var s=Basic("race"); s.Robots.Add(new("b",new(2,0,0),Heading.WEST,Fixture("race"))); s.Materials.Add(new("m",new(1,0,1),BuildType.WALL));
            var result=Run("race",s); Check(result.Code==0,result.Text); Check(result.Text.Contains("Tick 2 b WAIT OK"),result.Text);
        });
        test("PHP completed build task throws TaskInvalidatedException catchable as both base classes",()=>{
            var s=Basic("build-race");
            s.Robots.Add(new("b",new(2,0,0),Heading.WEST,Fixture("build-race")));
            s.Materials.AddRange([new("lower",new(1,0,1),BuildType.WALL),new("upper",new(1,0,2),BuildType.WALL)]);
            s.Tasks.Add(new("task",new(1,0,1),BuildType.WALL,BuildType.WALL));
            var result=Run("build-race",s); Check(result.Code==0,result.Text);
            Check(result.Text.Contains("Tick 3 a BUILD OK") && result.Text.Contains("Tick 4 b WAIT OK"),result.Text);
        });
        test("protocol binds actions to their owning robot",()=>{
            var s=Basic("spoof"); s.Robots.Add(new("b",new(2,0,0),Heading.WEST,Fixture("idle")));
            var result=Run("spoof",s); Check(result.Code==0,result.Text);
            using var data=System.Text.Json.JsonDocument.Parse(result.Result);
            var robots=data.RootElement.GetProperty("robots");
            Check(robots[0].GetProperty("position").GetProperty("x").GetInt32()==1 && robots[1].GetProperty("position").GetProperty("x").GetInt32()==2,"Robot impersonation succeeded");
        });
        test("protocol rejects privileged opcode",()=>{
            var result=Run("invalid",Basic("invalid")); Check(result.Code==1 && result.Text.Contains("Invalid protocol opcode"),result.Text);
        });
        test("PHP infinite loop is terminated with diagnostic",()=>{
            var result=Run("spin",Basic("spin")); Check(result.Code==1 && result.Text.Contains("timed out"),result.Text);
        });
        test("Library Navigation reaches flat target via detour and never enters wall",()=>{
            var s=Basic("navigation-flat"); s.MaxTicks=300;
            for(int x=0;x<=2;x++) s.Blocks.Add(new(new(x,1,0),BuildType.FLOOR));
            s.Blocks.Add(new(new(1,0,1),BuildType.WALL));
            var result=Run("navigation-flat",s); Check(result.Code==0,result.Text);
            using var data=System.Text.Json.JsonDocument.Parse(result.Result);
            var moves=data.RootElement.GetProperty("events").EnumerateArray().Where(e=>e.GetProperty("action").GetString()=="MOVE_FORWARD").ToArray();
            Check(moves.Length>0 && moves.All(e=>e.GetProperty("success").GetBoolean()),"Invalid cell was used as a route");
        });
        test("Library Navigation reaches upper floor and returns via stairs",()=>{
            var s=Basic("navigation-stairs");
            s.Blocks.AddRange([new(new(1,0,1),BuildType.STAIRS,Heading.EAST),new(new(2,0,1),BuildType.FLOOR)]);
            var result=Run("navigation-stairs",s); Check(result.Code==0,result.Text);
        });
        test("Library managers filter current snapshots and break distance ties deterministically",()=>{
            var s=Basic("managers");
            s.Materials.AddRange([new("b-material",new(0,1,1),BuildType.WALL),new("a-material",new(1,0,1),BuildType.WALL),new("0-window",new(0,-1,1),BuildType.WINDOW)]);
            s.Tasks.AddRange([new("b-task",new(0,1,2),BuildType.WALL,BuildType.WALL),new("a-task",new(1,0,2),BuildType.WALL,BuildType.WALL)]);
            s.Zones.AddRange([new("z-first","置き場1","MATERIAL_STORAGE",[new(1,0,0)]),new("a-zone","other","MATERIAL_STORAGE",[new(2,0,0)])]);
            var result=Run("managers",s); Check(result.Code==0,result.Text);
        });
        Scenario Storage(string program)
        {
            var s=Basic(program); s.MaxTicks=2000; s.Blocks.Clear();
            for(int y=0;y<=2;y++) for(int x=0;x<=4;x++) s.Blocks.Add(new(new(x,y,0),BuildType.FLOOR));
            s.Materials.AddRange([new("held",new(1,0,1),BuildType.WALL),new("mixed",new(1,1,1),BuildType.WINDOW),
                new("blocked",new(2,1,1),BuildType.WALL),new("pile",new(4,1,1),BuildType.WALL)]);
            s.Blocks.Add(new(new(2,1,2),BuildType.FLOOR));
            s.Zones.Add(new("storage","置き場1","MATERIAL_STORAGE",[new(1,1,0),new(2,1,0),new(3,1,0),new(4,1,0)]));
            return s;
        }
        test("Library Storage prioritizes same-material pile over nearer empty cell",()=>{
            var result=Run("storage-pile",Storage("storage-pile")); Check(result.Code==0,result.Text);
        });
        test("Library Storage skips mixed, obstructed and full stacks then uses empty cell",()=>{
            var s=Storage("storage-empty");
            for(int z=2;z<=4;z++) s.Materials.Add(new("full-"+z,new(4,1,z),BuildType.WALL));
            var result=Run("storage-empty",s); Check(result.Code==0,result.Text);
        });
        test("Library Storage returns no candidate when all zone cells are blocked",()=>{
            var s=Storage("storage-full");
            s.Zones[0]=s.Zones[0] with {Cells=[new(1,1,0),new(2,1,0)]};
            var result=Run("storage-full",s); Check(result.Code==0,result.Text);
        });
        test("Standard Builder and Carrier finish library scenario deterministically",()=>{
            var s=Json.Load(Path.Combine(root,"scenarios","library.json"));
            s.Robots=s.Robots.Select(r=>r with {Program=Path.GetFullPath(r.Program,Path.Combine(root,"scenarios"))}).ToList();
            var result=Run("library",s); Check(result.Code==0,result.Text);
            var repeat=Run("library-repeat",s); Check(repeat.Code==0 && result.Result==repeat.Result,repeat.Text);
            using var data=System.Text.Json.JsonDocument.Parse(result.Result);
            Check(data.RootElement.GetProperty("tasks").EnumerateArray().All(t=>t.GetProperty("status").GetString()=="COMPLETED"),"Builder left tasks unfinished");
            var delivered=data.RootElement.GetProperty("materials").EnumerateArray().Single(m=>m.GetProperty("id").GetString()=="delivery").GetProperty("position");
            Check(delivered.GetProperty("x").GetInt32()==4 && new[]{4,5}.Contains(delivered.GetProperty("y").GetInt32()),"Carrier did not deliver to storage");
            var actions=data.RootElement.GetProperty("events").EnumerateArray().Select(e=>e.GetProperty("action").GetString()).ToHashSet();
            Check(new[]{"MOVE_FORWARD","PICKUP","DROP","BUILD"}.All(actions.Contains),"Missing standard-library work actions");
        });
        test("Standard Builder recovers from completed task and stores unused material",()=>{
            var s=Basic("builder-race"); s.MaxTicks=300;
            s.Robots.Add(new("b",new(2,0,0),Heading.WEST,Fixture("builder-race")));
            s.Blocks.Add(new(new(2,1,0),BuildType.FLOOR));
            s.Zones.Add(new("storage","置き場1","MATERIAL_STORAGE",[new(2,1,0)]));
            s.Materials.AddRange([new("lower",new(1,0,1),BuildType.WALL),new("upper",new(1,0,2),BuildType.WALL)]);
            s.Tasks.Add(new("task",new(1,0,1),BuildType.WALL,BuildType.WALL));
            var result=Run("builder-race",s); Check(result.Code==0,result.Text);
            Check(result.Text.Contains("TaskInvalidated:") && result.Text.Contains("b DROP OK"),result.Text);
            using var data=System.Text.Json.JsonDocument.Parse(result.Result);
            Check(data.RootElement.GetProperty("tasks")[0].GetProperty("status").GetString()=="COMPLETED", "Task not completed");
            Check(data.RootElement.GetProperty("robots").EnumerateArray().All(r=>r.GetProperty("carrying").GetArrayLength()==0),"Builder retained unused inventory");
        });
    }
}
