#define ENABLE_VALIDATION_LAYERS

using Meteora.Window;

namespace Meteora;

using Meteora.Extensions;

using System.Runtime.InteropServices;

using Vulkan;

using static Vulkan.Instance;

public class MeteoraApp : IDisposable
{
	private bool _disposedValue;
	private Instance? _instance;
	private PhysicalDevice? _physicalDevice;
	private readonly MeteoraWindow _window;
	private Device? _device;
	private Queue? _graphicsQueue;
	private Queue? _presentQueue;
	private SurfaceKhr? _surface;
	private SwapchainKhr? _swapchain;
	private Image[]? _swapImages;
	private Format _curSwapChainFormat;
	private Extent2D _curSwapChainExtent;
	private ImageView[]? _swapchainImageViews;

	private readonly string[] _validationLayers = new[]
	{
		"VK_LAYER_KHRONOS_validation",
	};

	private readonly string[] _deviceExtensions = new[]
	{
		"VK_KHR_swapchain",
	};

	public MeteoraApp(MeteoraWindow window)
	{
		_window = window;
	}

	public void Run()
	{
		_window.Init();
		InitVulkan();
		MainLoop();
		Cleanup();
	}

	private void InitVulkan()
	{
		CreateInstance();
#if ENABLE_VALIDATION_LAYERS
		SetupDebugMessenger();
#endif
		PrepareSurface();
		PickPhysicalDevice();
		CreateLogicalDevice();
		CreateSwapChain();
		CreateImageViews();
		CreateGraphicsPipeline();
	}

	private void PrepareSurface()
	{
		var instancePtr = ((IMarshalling)_instance!).Handle;
		_ = _window.GetSurface(instancePtr, out var surfacePtr);

		var surface = Activator.CreateInstance(typeof(SurfaceKhr), true) as SurfaceKhr;
		var handleField = typeof(SurfaceKhr).GetField("m", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		handleField!.SetValue(surface, (ulong)surfacePtr);

		_surface = surface;
	}

	#region Graphics Pipeline

	private void CreateGraphicsPipeline()
	{
	}

	#endregion Graphics Pipeline

	#region Device

	private void CreateLogicalDevice()
	{
		if (_physicalDevice == null)
			throw new Exception("Physical device is not set");
		if (_surface == null)
			throw new Exception("Surface not created");

		var indexFamilies = _physicalDevice.FindQueueFamilies(_surface!);
		var indices = indexFamilies.UniqueIndices;

		var queueInfo = new DeviceQueueCreateInfo[indices.Length];
		for (int i = 0; i < queueInfo.Length; i++)
		{
			queueInfo[i] = new DeviceQueueCreateInfo
			{
				QueueFamilyIndex = indices[i],
				QueueCount = 1,
				QueuePriorities = new[] { 1f }
			};
		}

		var deviceFeatures = new PhysicalDeviceFeatures();

		var deviceCreateInfo = new DeviceCreateInfo
		{
			QueueCreateInfos = queueInfo,
			QueueCreateInfoCount = (uint)queueInfo.Length,
			EnabledFeatures = deviceFeatures,
			EnabledExtensionNames = _deviceExtensions,
			EnabledExtensionCount = (uint)_deviceExtensions.Length,
#if ENABLE_VALIDATION_LAYERS
			EnabledLayerNames = _validationLayers,
			EnabledLayerCount = (uint)_validationLayers.Length,
#else
			EnabledLayerCount = 0,
#endif
		};

		_device = _physicalDevice.CreateDevice(deviceCreateInfo);
		_graphicsQueue = _device.GetQueue((uint)indexFamilies.graphics!, 0);
		_presentQueue = _device.GetQueue((uint)indexFamilies.presentation!, 0);
	}

	private void PickPhysicalDevice()
	{
		if (_surface == null)
			throw new Exception("Surface not created");

		var devices = _instance!.EnumeratePhysicalDevices();
		var suitableDevices = devices.Where(d => d.IsDeviceSuitable(_surface, _deviceExtensions)).OrderBy(GetDeviceScore);

		static int GetDeviceScore(PhysicalDevice dev)
		{
			var devProps = dev.GetProperties();
			var features = dev.GetFeatures();
			var score = 0;
			if (devProps.DeviceType == PhysicalDeviceType.DiscreteGpu)
				score += 1000;

			score += (int)devProps.Limits.MaxImageDimension2D;
			return 0;
		}

		var best = suitableDevices.FirstOrDefault() ?? throw new Exception("Failed to find a suitable GPU");
		_physicalDevice = best;
	}

	#endregion Device

	#region Swap chain

	private void CreateSwapChain()
	{
		if (_physicalDevice == null)
			throw new Exception("Physical device is not set");
		if (_surface == null)
			throw new Exception("Surface not created");
		if (_device == null)
			throw new Exception("Logical Device not created");

		var swapSupport = _physicalDevice.QuerySwapChainSupport(_surface);

		var format = swapSupport.ChooseSwapSurfaceFormat();
		var presentMode = swapSupport.ChooseSwapPresentMode();

		var (width, height) = _window.GetFrameBufferSize();
		var extent = swapSupport.ChooseSwapExtent(width, height);

		var imgCount = swapSupport.capabilities.MinImageCount + 1;
		if (swapSupport.capabilities.MaxImageCount > 0 && imgCount > swapSupport.capabilities.MaxImageCount)
			imgCount = swapSupport.capabilities.MaxImageCount;

		var createInfo = new SwapchainCreateInfoKhr
		{
			Surface = _surface,
			MinImageCount = imgCount,
			ImageFormat = format.Format,
			ImageColorSpace = format.ColorSpace,
			ImageExtent = extent,
			ImageArrayLayers = 1,
			ImageUsage = ImageUsageFlags.ColorAttachment,
			PreTransform = swapSupport.capabilities.CurrentTransform,
			CompositeAlpha = CompositeAlphaFlagsKhr.Opaque,
			PresentMode = presentMode,
			Clipped = true,
			OldSwapchain = null
		};

		var indices = _physicalDevice.FindQueueFamilies(_surface);
		var uniqueIndices = indices.UniqueIndices;
		if (indices.graphics != indices.presentation)
		{
			createInfo.ImageSharingMode = SharingMode.Concurrent;
			createInfo.QueueFamilyIndexCount = (uint)uniqueIndices.Length;
			createInfo.QueueFamilyIndices = uniqueIndices;
		}
		else
		{
			createInfo.ImageSharingMode = SharingMode.Exclusive;
		}

		_swapchain = _device.CreateSwapchainKHR(createInfo);

		_swapImages = _device.GetSwapchainImagesKHR(_swapchain);

		_curSwapChainFormat = format.Format;
		_curSwapChainExtent = extent;
	}

	private void CreateImageViews()
	{
		if (_swapImages == null)
			throw new Exception("Swap images is not set");
		if (_device == null)
			throw new Exception("Logical Device is not set");

		_swapchainImageViews = new ImageView[_swapImages.Length];

		for (int i = 0; i < _swapchainImageViews.Length; i++)
		{
			var createInfo = new ImageViewCreateInfo
			{
				Image = _swapImages[i],
				ViewType = ImageViewType.View2D,
				Format = _curSwapChainFormat,
				Components = new ComponentMapping
				{
					R = ComponentSwizzle.Identity,
					B = ComponentSwizzle.Identity,
					G = ComponentSwizzle.Identity,
					A = ComponentSwizzle.Identity,
				},
				SubresourceRange = new ImageSubresourceRange
				{
					AspectMask = ImageAspectFlags.Color,
					BaseArrayLayer = 0,
					BaseMipLevel = 0,
					LevelCount = 1,
					LayerCount = 1
				}
			};

			_swapchainImageViews[i] = _device.CreateImageView(createInfo);
		}
	}

	#endregion Swap chain

	private void CreateInstance()
	{
#if ENABLE_VALIDATION_LAYERS
		CheckValiationLayersSupport();
#endif

		var appInfo = new ApplicationInfo
		{
			ApplicationName = _window.Title,
			ApplicationVersion = Version.Make(0, 0, 0),
			EngineName = "Meteora",
			EngineVersion = Version.Make(0, 0, 0),
			ApiVersion = Version.Make(1, 3, 242),
		};

		var exts = GetRequiredExtensions();
		var createInfo = new InstanceCreateInfo
		{
			ApplicationInfo = appInfo,
			EnabledExtensionCount = (uint)exts.Length,
			EnabledExtensionNames = exts,
#if ENABLE_VALIDATION_LAYERS
			EnabledLayerCount = (uint)_validationLayers.Length,
			EnabledLayerNames = _validationLayers
#else
			EnabledLayerCount = 0,
#endif
		};

		_instance = new Instance(createInfo);
	}

	private void MainLoop()
	{
		while (!_window.ShouldClose)
		{
			_window.PollEvents();
		}
	}

	#region Validation Layers

	private string[] GetRequiredExtensions()
	{
		var exts = _window.GetRequiredInstanceExtensions();

#if ENABLE_VALIDATION_LAYERS
		exts = exts.Append("VK_EXT_debug_report").ToArray();
#endif
		return exts;
	}

	private void CheckValiationLayersSupport()
	{
		var props = Commands.EnumerateInstanceLayerProperties();
		var unsupported = _validationLayers.Where(v => !props.Any(p => p.LayerName == v));
		if (unsupported.Any())
			throw new Exception($"The requested validation layers are not suported\n{string.Join("\n\t", unsupported)}");
	}

	private void SetupDebugMessenger()
	{
		var debugCallback = new DebugReportCallback(DebugReportCallback);
		_instance!.EnableDebug(debugCallback);
	}

	private static Bool32 DebugReportCallback(DebugReportFlagsExt flags, DebugReportObjectTypeExt objectType, ulong objectHandle, IntPtr location, int messageCode, IntPtr layerPrefix, IntPtr message, IntPtr userData)
	{
		string? layerString = Marshal.PtrToStringAnsi(layerPrefix);
		string? messageString = Marshal.PtrToStringAnsi(message);

		System.Console.WriteLine("[Debug] [{0}]: {1}", layerString, messageString);

		return false;
	}

	#endregion Validation Layers

	#region Cleaup

	private void Cleanup()
	{
		if (_swapchainImageViews != null)
		{
			for (int i = 0; i < _swapchainImageViews.Length; i++)
				_device?.DestroyImageView(_swapchainImageViews[i]);
		}
		_device?.DestroySwapchainKHR(_swapchain);
		_instance?.DestroySurfaceKHR(_surface);
		_device?.Destroy();
		_instance?.Dispose();
		_window.Dispose();
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
			}
			Cleanup();
			_disposedValue = true;
		}
	}

	~MeteoraApp()
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

	#endregion Cleaup
}