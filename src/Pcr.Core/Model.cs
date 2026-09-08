using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pcr.Core;

public enum BuildType { FLOOR, WALL, PILLAR, WINDOW, STAIRS }
public enum Heading { NORTH, EAST, SOUTH, WEST }
public enum ActionKind { MOVE_FORWARD, TURN_LEFT, TURN_RIGHT, PICKUP, DROP, BUILD, WAIT }
public readonly record struct Position(int X, int Y, int Z)
{
    public Position Ahead(Heading h) => h switch
    {
        Heading.NORTH => this with { Y = Y - 1 }, Heading.EAST => this with { X = X + 1 },
        Heading.SOUTH => this with { Y = Y + 1 }, _ => this with { X = X - 1 }
    };
    public Position Up(int n) => this with { Z = Z + n };
}
public sealed record Block(Position Position, BuildType Type, Heading Direction = Heading.NORTH);
public sealed record Material(string Id, Position Position, BuildType Type, int Units = 1);
public sealed record BuildTask(string Id, Position Position, BuildType Type, BuildType RequiredMaterial,
    int RequiredUnits = 1, string Status = "PENDING", Heading Direction = Heading.NORTH);
public sealed record Zone(string Id, string Name, string Type, Position[] Cells);
public sealed record RobotSpec(string Id, Position Position, Heading Direction, string Program, int Capacity = 1);
public sealed class Scenario
{
    public int Seed { get; set; }
    public int MaxHeight { get; set; } = 4;
    public int MaxTicks { get; set; } = 1000;
    public List<Block> Blocks { get; set; } = [];
    public List<Material> Materials { get; set; } = [];
    public List<BuildTask> Tasks { get; set; } = [];
    public List<Zone> Zones { get; set; } = [];
    public List<RobotSpec> Robots { get; set; } = [];
    public Dictionary<ActionKind, int> ActionCosts { get; set; } = [];
}
public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public static string Encode(object? value) => JsonSerializer.Serialize(value, Options);
    public static Scenario Load(string file) => JsonSerializer.Deserialize<Scenario>(File.ReadAllText(file), Options)
        ?? throw new ArgumentException("Empty scenario");
}
public sealed record RobotInfo(string Id, Position Position, Heading Direction, string Status, Material[] Carrying, int Capacity);
public sealed record CellInfo(int Offset, Position Position, Block? Block, BuildTask? Task, Zone? Zone, Material? Material, RobotInfo? Robot);
public sealed record SpaceInfo(RobotInfo? Robot, Material[] Materials, BuildTask[] Tasks);
public sealed record ScanInfo(bool IsMovable, Position? Destination, int? ElevationDelta, CellInfo Floor, SpaceInfo Space, CellInfo[] Cells);
public sealed record ActionResult(string RobotId, ActionKind Action, bool Success, string? Error, long Tick, int Cost, string? Entity = null, Position? Target = null);
public sealed class RuleException(string message) : Exception(message);
