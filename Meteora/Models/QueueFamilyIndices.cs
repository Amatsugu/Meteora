namespace Meteora.Models;

public struct QueueFamilyIndices
{
	public uint? graphics;
	public uint? presentation;

	public readonly uint[] UniqueIndices => new[] { (uint)graphics!, (uint)presentation! }.Distinct().ToArray();
	public readonly bool IsComplete => graphics != null && presentation != null;
}