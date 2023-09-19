namespace Meteora.Models;

public readonly struct QueueFamilyIndices
{
	public readonly uint graphics;
	public readonly uint presentation;
	public readonly bool isComplete;

	public QueueFamilyIndices(uint graphics, uint presentation)
	{
		this.graphics = graphics;
		this.presentation = presentation;
		this.isComplete = true;
	}

	public readonly uint[] UniqueIndices => new[] { graphics, presentation }.Distinct().ToArray();
}