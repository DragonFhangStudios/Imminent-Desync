public enum BuildTool
{
	None,       // No tool: legacy sandbox brush controls
	Source,     // Sandbox only: places an Emitter
	Stabilizer,
	Nullifier,  // formerly "Neutralizer" (same enum index, so serialized values are preserved)
	Core
}
