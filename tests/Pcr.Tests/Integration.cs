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
    }
}
