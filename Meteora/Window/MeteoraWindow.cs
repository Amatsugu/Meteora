using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meteora.Window;
public abstract class MeteoraWindow : IDisposable
{
	public abstract event Action<IntPtr, int, int>? OnSetFrameBufferSize;
	public bool IsInit { get; set; }
	public int Height { get; set; }
	public int Width { get; set; }
	public string Title { get; protected set; }
	public abstract bool ShouldClose { get; }

	private bool _disposedValue;

	public MeteoraWindow(int width, int height, string title = "Meteora Window")
	{
		Height = height;
		Width = width;
		Title = title;
	}

	public abstract void Init();

	public abstract void SetTitle(string title);

	public abstract int GetSurface(nint instancePtr, out nint surfacePtr);

	protected abstract void Cleanup();
	public abstract void PollEvents();
	public abstract void WaitEvents();

	public abstract string[] GetRequiredInstanceExtensions();

	public abstract (uint width, uint height) GetFrameBufferSize();

	protected virtual void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				// TODO: dispose managed state (managed objects)
			}

			Cleanup();

			_disposedValue = true;
		}
	}

	~MeteoraWindow()
	{
		// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
