using GLFW;

using System.Runtime.InteropServices;

namespace Meteora.Window;

public class GlfwWindow : MeteoraWindow
{
	private GLFW.Window _window;

	public override bool ShouldClose => Glfw.WindowShouldClose(_window);

	public GlfwWindow(int width, int height, string title) : base(width, height, title)
	{
	}

	public override int GetSurface(nint instance, out nint surfacePtr)
	{
		var handleField = typeof(GLFW.Window).GetField("handle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		var windowHandle = (IntPtr)handleField!.GetValue(_window)!;
		var r = GLFW.Vulkan.CreateWindowSurface(instance, windowHandle, nint.Zero, out var ptr);
		surfacePtr = (nint)ptr;
		return r;
	}

	public override void Init()
	{
		Glfw.Init();
		
		Glfw.WindowHint(Hint.ClientApi, ClientApi.None);
		Glfw.WindowHint(Hint.Resizable, false);

		_window = Glfw.CreateWindow(Width, Height, Title, GLFW.Monitor.None, GLFW.Window.None);
	}

	public override void SetTitle(string title)
	{
		Title = title;
		Glfw.SetWindowTitle(_window, title);
	}

	public override void PollEvents()
	{
		Glfw.PollEvents();
	}

	public override (uint width, uint height) GetFrameBufferSize()
	{
		Glfw.GetFramebufferSize(_window, out var width, out var height);
		return ((uint)width, (uint)height);
	}

	protected override void Cleanup()
	{
		Glfw.DestroyWindow(_window);
		Glfw.Terminate();
	}

	[DllImport(Glfw.LIBRARY, EntryPoint = "glfwGetRequiredInstanceExtensions", CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr GetRequiredInstanceExtensions(out uint count);

	public override string[] GetRequiredInstanceExtensions()
	{
		var ptr = GetRequiredInstanceExtensions(out var count);
		var extensions = new string?[count];
		if (count > 0 && ptr != IntPtr.Zero)
		{
			var offset = 0;
			for (var i = 0; i < count; i++, offset += IntPtr.Size)
			{
				var p = Marshal.ReadIntPtr(ptr, offset);
				extensions[i] = Marshal.PtrToStringAnsi(p);
			}
		}

		return extensions.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()!;
	}
}