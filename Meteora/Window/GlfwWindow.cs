using GLFW;

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
		return GLFW.Vulkan.CreateWindowSurface(instance, windowHandle, nint.Zero, out surfacePtr);
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

	public override string[] GetRequiredInstanceExtensions()
	{
		return new string[] { "VK_KHR_surface", "VK_KHR_win32_surface" };
		//TODO: figure out why this fails
		//return GLFW.Vulkan.GetRequiredInstanceExtensions();
	}
}