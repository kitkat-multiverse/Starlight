namespace Starlight.Game.Modules;

/// <summary>
/// Marker for a per-player unit of state and packet handlers. Components contribute modules;
/// each player owns one instance of every registered module. The
/// <see cref="Starlight.CodeGen.Component.ComponentModuleGenerator"/> discovers implementors and
/// wires their <c>[Opcode]</c> handlers into the <see cref="ModuleRegistry"/>.
/// </summary>
public interface IModule;
