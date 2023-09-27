//#define ENABLE_VALIDATION_LAYERS

using Meteora.Window;

namespace Meteora;

using Meteora.Extensions;
using Meteora.Models;

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
	private Image[] _swapImages = Array.Empty<Image>();
	private Format _curSwapChainFormat;
	private Extent2D _curSwapChainExtent;
	private ImageView[] _swapchainImageViews = Array.Empty<ImageView>();
	private PipelineLayout? _pipelineLayout;
	private RenderPass? _renderPass;
	private Pipeline? _graphicsPipeline;
	private ShaderModule[] _shaderModules = Array.Empty<ShaderModule>();
	private Framebuffer[] _framebuffers = Array.Empty<Framebuffer>();
	private CommandPool? _commandPool;
	private CommandBuffer[] _commandBuffers = Array.Empty<CommandBuffer>();
	private Semaphore[] _imageAvailableSemaphores = Array.Empty<Semaphore>();
	private Semaphore[] _renderFinishedSemaphores = Array.Empty<Semaphore>();
	private Fence[] _inFlightFences = Array.Empty<Fence>();
	public const uint VK_SUBPASS_EXTERNAL = ~0u;
	public const int MAX_FRAMES_IN_FLIGHT = 2;

	private bool _cleaning = false;
	private int _curFrame = 0;
	private bool _needResize = false;

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
		_instance = CreateInstance();
		_window.OnSetFrameBufferSize += SetFrameBufferSize;

#if ENABLE_VALIDATION_LAYERS
		SetupDebugMessenger(_instance);
#endif
		_surface = PrepareSurface(_instance);

		//Device
		_physicalDevice = PickPhysicalDevice(_instance, _surface);
		var indexFamilies = _physicalDevice.FindQueueFamilies(_surface);
		_device = CreateLogicalDevice(_physicalDevice, _surface, indexFamilies);
		_graphicsQueue = _device.GetQueue(indexFamilies.graphics!, 0);
		_presentQueue = _device.GetQueue(indexFamilies.presentation!, 0);

		//Swapchain
		(_swapchain, _curSwapChainFormat, _curSwapChainExtent) = CreateSwapChain(_physicalDevice, _surface, _device);
		_swapImages = _device.GetSwapchainImagesKHR(_swapchain);
		_swapchainImageViews = CreateImageViews(_device, _swapImages);

		//Graphics Pipeline
		_renderPass = CreateRenderPass(_device, _curSwapChainFormat);
		(_pipelineLayout, _graphicsPipeline, _shaderModules) = CreateGraphicsPipeline(_device, _curSwapChainExtent, _renderPass);

		//Drawing
		_framebuffers = CreateFrameBuffers(_device, _swapchainImageViews, _renderPass, _curSwapChainExtent);
		_commandPool = CreateCommandPool(_device, indexFamilies);
		_commandBuffers = CreateCommandBuffers(_device, _commandPool);

		(_imageAvailableSemaphores, _renderFinishedSemaphores, _inFlightFences) = CreateSyncObjects(_device);
	}

	private SurfaceKhr PrepareSurface(Instance instance)
	{
		var instancePtr = ((IMarshalling)instance!).Handle;
		_ = _window.GetSurface(instancePtr, out var surfacePtr);

		var surface = Activator.CreateInstance(typeof(SurfaceKhr), true) as SurfaceKhr ?? throw new Exception("Failed to activate surface handle wrapper");
		var handleField = typeof(SurfaceKhr).GetField("m", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		handleField!.SetValue(surface, (ulong)surfacePtr);

		return surface;
	}

	private void SetFrameBufferSize(IntPtr window, int width, int height)
	{
		_needResize = true;
	}

	#region Drawing

	private (Semaphore[] img, Semaphore[] render, Fence[] inFlight) CreateSyncObjects(Device device)
	{
		var img = new Semaphore[MAX_FRAMES_IN_FLIGHT]; ;
		var render = new Semaphore[MAX_FRAMES_IN_FLIGHT]; ;
		var fences = new Fence[MAX_FRAMES_IN_FLIGHT];
		for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
		{
			img[i] = device.CreateSemaphore(new());
			render[i] = device.CreateSemaphore(new());
			fences[i] = device.CreateFence(new() { Flags = FenceCreateFlags.Signaled });
		}

		return (img, render, fences);
	}

	private static Framebuffer[] CreateFrameBuffers(Device device, ImageView[] swapchainViews, RenderPass renderPass, Extent2D extent)
	{
		var swapchainBuffers = new Framebuffer[swapchainViews.Length];

		for (int i = 0; i < swapchainViews.Length; i++)
		{
			var attachements = new[] { swapchainViews[i] };
			var frameBufferInfo = new FramebufferCreateInfo
			{
				RenderPass = renderPass,
				AttachmentCount = 1,
				Attachments = attachements,
				Width = extent.Width,
				Height = extent.Height,
				Layers = 1
			};

			swapchainBuffers[i] = device.CreateFramebuffer(frameBufferInfo);
		}

		return swapchainBuffers;
	}

	private static CommandPool CreateCommandPool(Device device, QueueFamilyIndices indices)
	{
		var poolInfo = new CommandPoolCreateInfo
		{
			QueueFamilyIndex = (uint)indices.graphics!,
			Flags = CommandPoolCreateFlags.ResetCommandBuffer
		};

		var commandPool = device.CreateCommandPool(poolInfo);
		return commandPool;
	}

	private static CommandBuffer[] CreateCommandBuffers(Device device, CommandPool commandPool)
	{
		var allocInfo = new CommandBufferAllocateInfo
		{
			CommandPool = commandPool,
			CommandBufferCount = MAX_FRAMES_IN_FLIGHT,
			Level = CommandBufferLevel.Primary
		};

		return device.AllocateCommandBuffers(allocInfo);
	}

	private static void RecordCommandBuffer(CommandBuffer commandBuffer, Framebuffer framebuffer, RenderPass renderPass, Extent2D extent, Pipeline graphicsPipeline)
	{
		var beginInfo = new CommandBufferBeginInfo
		{
		};

		commandBuffer.Begin(beginInfo);

		var clearvalue = new ClearValue
		{
			Color = new ClearColorValue
			{
				Float32 = new[] { .01f, 0f, .01f, 1f }
			}
		};

		var renderPassInfo = new RenderPassBeginInfo
		{
			RenderPass = renderPass,
			Framebuffer = framebuffer,
			RenderArea = new Rect2D { Offset = new Offset2D(), Extent = extent },
			ClearValueCount = 1,
			ClearValues = new[] { clearvalue }
		};

		commandBuffer.CmdBeginRenderPass(renderPassInfo, SubpassContents.Inline);
		commandBuffer.CmdBindPipeline(PipelineBindPoint.Graphics, graphicsPipeline);

		var viewport = new Viewport
		{
			X = 0,
			Y = 0,
			Width = extent.Width,
			Height = extent.Height,
			MinDepth = 0,
			MaxDepth = 1,
		};

		commandBuffer.CmdSetViewport(0, viewport);

		var scissor = new Rect2D
		{
			Extent = extent,
			Offset = new Offset2D()
		};

		commandBuffer.CmdSetScissor(0, scissor);

		commandBuffer.CmdDraw(3, 1, 0, 0);

		commandBuffer.CmdEndRenderPass();
		commandBuffer.End();
	}

	private void DrawFrame(double deltaTime)
	{
		_device!.WaitForFence(_inFlightFences[_curFrame], true, ulong.MaxValue);

		try
		{
			var imageIndex = _device.AcquireNextImageKHR(_swapchain!, ulong.MaxValue, _imageAvailableSemaphores[_curFrame]);
			_device.ResetFence(_inFlightFences[_curFrame]);

			_commandBuffers[_curFrame].Reset();
			RecordCommandBuffer(_commandBuffers[_curFrame], _framebuffers![imageIndex], _renderPass!, _curSwapChainExtent!, _graphicsPipeline!);

			var signalSemaphores = new[] { _renderFinishedSemaphores[_curFrame] };

			var submitInfo = new SubmitInfo
			{
				WaitSemaphoreCount = 1,
				WaitSemaphores = new[] { _imageAvailableSemaphores[_curFrame] },
				WaitDstStageMask = new[] { PipelineStageFlags.ColorAttachmentOutput },
				CommandBufferCount = 1,
				CommandBuffers = new[] { _commandBuffers[_curFrame] },
				SignalSemaphoreCount = (uint)signalSemaphores.Length,
				SignalSemaphores = signalSemaphores
			};

			_graphicsQueue!.Submit(submitInfo, _inFlightFences[_curFrame]);

			var presentInfo = new PresentInfoKhr
			{
				WaitSemaphoreCount = (uint)signalSemaphores.Length,
				WaitSemaphores = signalSemaphores,
				Swapchains = new[] { _swapchain },
				SwapchainCount = 1,
				ImageIndices = new[] { imageIndex },
			};

			_presentQueue!.PresentKHR(presentInfo);

			_curFrame = (_curFrame + 1) % MAX_FRAMES_IN_FLIGHT;
		}
		catch (Vulkan.ResultException ex)
		{
			Console.WriteLine($"Failed to acquire next image {ex.Result}, recreating swapchain");
			RecreateSwapchain(_device!, _physicalDevice!, _surface!, _renderPass!);
		}
	}

	#endregion Drawing

	#region Graphics Pipeline

	private static RenderPass CreateRenderPass(Device device, Format format)
	{
		var attachmentDescription = new AttachmentDescription
		{
			Format = format,
			Samples = SampleCountFlags.Count1,
			LoadOp = AttachmentLoadOp.Clear,
			StoreOp = AttachmentStoreOp.Store,
			StencilLoadOp = AttachmentLoadOp.DontCare,
			StencilStoreOp = AttachmentStoreOp.DontCare,
			InitialLayout = ImageLayout.Undefined,
			FinalLayout = ImageLayout.PresentSrcKhr,
		};

		var colorAttachmentRef = new AttachmentReference
		{
			Attachment = 0,
			Layout = ImageLayout.ColorAttachmentOptimal
		};

		var subpassDependency = new SubpassDependency
		{
			SrcSubpass = VK_SUBPASS_EXTERNAL, //Find value
			DstSubpass = 0,
			SrcStageMask = PipelineStageFlags.ColorAttachmentOutput,
			SrcAccessMask = 0,
			DstStageMask = PipelineStageFlags.ColorAttachmentOutput,
			DstAccessMask = AccessFlags.ColorAttachmentWrite
		};

		var subpass = new SubpassDescription
		{
			PipelineBindPoint = PipelineBindPoint.Graphics,
			ColorAttachmentCount = 1,
			ColorAttachments = new[] { colorAttachmentRef },
		};

		var renderPassCreateInfo = new RenderPassCreateInfo
		{
			AttachmentCount = 1,
			Attachments = new[] { attachmentDescription },
			SubpassCount = 1,
			Subpasses = new[] { subpass },
			Dependencies = new[] { subpassDependency },
			DependencyCount = 1,
		};

		return device.CreateRenderPass(renderPassCreateInfo);
	}

	private (PipelineLayout pipelineLayout, Pipeline graphicsPipeline, ShaderModule[] shaderModules) CreateGraphicsPipeline(Device device, Extent2D extent, RenderPass renderPass)
	{
		var vertModule = device.CreateShaderModule(File.ReadAllBytes("Shaders/spv/vert.spv"));
		var fragModule = device.CreateShaderModule(File.ReadAllBytes("Shaders/spv/frag.spv"));

		var shaderModules = new[] { vertModule, fragModule };

		var vertShaderStageInfo = new PipelineShaderStageCreateInfo
		{
			Stage = ShaderStageFlags.Vertex,
			Module = vertModule,
			Name = "main",
		};

		var fragShaderStageInfo = new PipelineShaderStageCreateInfo
		{
			Stage = ShaderStageFlags.Fragment,
			Module = fragModule,
			Name = "main"
		};

		var shaderStages = new[]
		{
			vertShaderStageInfo,
			fragShaderStageInfo,
		};

		var vertInputInfo = new PipelineVertexInputStateCreateInfo
		{
			VertexAttributeDescriptionCount = 0,
			VertexBindingDescriptionCount = 0
		};

		var inputAssemblyInfo = new PipelineInputAssemblyStateCreateInfo
		{
			Topology = PrimitiveTopology.TriangleList,
			PrimitiveRestartEnable = false
		};

		var viewPort = new Viewport
		{
			X = 0,
			Y = 0,
			Width = extent.Width,
			Height = extent.Height,
			MaxDepth = 0,
			MinDepth = 1
		};

		var scissor = new Rect2D
		{
			Offset = new Offset2D { X = 0, Y = 0 },
			Extent = extent
		};

		var dynamicStateInfo = new PipelineDynamicStateCreateInfo
		{
			DynamicStateCount = 2,
			DynamicStates = new[] { DynamicState.Viewport, DynamicState.Scissor }
		};

		var viewportStateInfo = new PipelineViewportStateCreateInfo
		{
			ViewportCount = 1,
			Viewports = new[] { viewPort },
			ScissorCount = 1,
			Scissors = new[] { scissor },
		};

		var rasterizer = new PipelineRasterizationStateCreateInfo
		{
			DepthClampEnable = false,
			RasterizerDiscardEnable = false,
			PolygonMode = PolygonMode.Fill,
			LineWidth = 1,
			CullMode = CullModeFlags.Back,
			FrontFace = FrontFace.Clockwise,
			DepthBiasEnable = false
		};

		var multisamplingInfo = new PipelineMultisampleStateCreateInfo
		{
			SampleShadingEnable = false,
			RasterizationSamples = SampleCountFlags.Count1,
			//Optional
			MinSampleShading = 1,
			AlphaToCoverageEnable = false,
			AlphaToOneEnable = false,
		};

		var colorBlendAttachment = new PipelineColorBlendAttachmentState
		{
			ColorWriteMask = ColorComponentFlags.R | ColorComponentFlags.G | ColorComponentFlags.B | ColorComponentFlags.A,
			BlendEnable = false,
		};

		var colorBlendingInfo = new PipelineColorBlendStateCreateInfo
		{
			LogicOpEnable = false,
			AttachmentCount = 1,
			Attachments = new[] { colorBlendAttachment }
		};

		var piplelineLayoutInfo = new PipelineLayoutCreateInfo
		{
			SetLayoutCount = 0,
		};

		var pipelineLayout = device.CreatePipelineLayout(piplelineLayoutInfo);

		var pipelineInfo = new GraphicsPipelineCreateInfo
		{
			StageCount = 2,
			Stages = shaderStages,
			VertexInputState = vertInputInfo,
			InputAssemblyState = inputAssemblyInfo,
			ViewportState = viewportStateInfo,
			RasterizationState = rasterizer,
			MultisampleState = multisamplingInfo,
			ColorBlendState = colorBlendingInfo,
			DynamicState = dynamicStateInfo,
			Layout = pipelineLayout,
			RenderPass = renderPass,
			Subpass = 0
		};

		var graphicsPipeline = device.CreateGraphicsPipelines(null, new[] { pipelineInfo });

		return (pipelineLayout, graphicsPipeline.First(), shaderModules);
	}

	#endregion Graphics Pipeline

	#region Device

	private PhysicalDevice PickPhysicalDevice(Instance instance, SurfaceKhr surface)
	{
		var devices = instance!.EnumeratePhysicalDevices();
		var suitableDevices = devices.Where(d => d.IsDeviceSuitable(surface, _deviceExtensions)).OrderBy(GetDeviceScore);

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
		return best;
	}

	private Device CreateLogicalDevice(PhysicalDevice physicalDevice, SurfaceKhr surface, QueueFamilyIndices queueFamilyIndices)
	{
		var indices = queueFamilyIndices.UniqueIndices;

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

		var device = physicalDevice.CreateDevice(deviceCreateInfo);

		return device;
	}

	#endregion Device

	#region Swap chain

	private void RecreateSwapchain(Device device, PhysicalDevice physicalDevice, SurfaceKhr surface, RenderPass renderPass)
	{
		var (width, height) = _window.GetFrameBufferSize();
		while (width == 0 || height == 0)
		{
			(width, height) = _window.GetFrameBufferSize();
			_window.WaitEvents();
		}
		device.WaitIdle();

		(_swapchain, _curSwapChainFormat, _curSwapChainExtent) = CreateSwapChain(physicalDevice, surface, device);
		_swapImages = device.GetSwapchainImagesKHR(_swapchain);
		_swapchainImageViews = CreateImageViews(device, _swapImages);
		_framebuffers = CreateFrameBuffers(device, _swapchainImageViews, renderPass, _curSwapChainExtent);
	}

	private (SwapchainKhr swapchain, Format format, Extent2D extent) CreateSwapChain(PhysicalDevice physicalDevice, SurfaceKhr surface, Device device, SwapchainKhr? oldSwapchain = null)
	{
		var (width, height) = _window.GetFrameBufferSize();

		var swapSupport = physicalDevice.QuerySwapChainSupport(surface);

		var format = swapSupport.ChooseSwapSurfaceFormat();
		var presentMode = swapSupport.ChooseSwapPresentMode();

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

		var indices = physicalDevice.FindQueueFamilies(surface);
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
		createInfo.OldSwapchain = oldSwapchain;

		var swapchain = device.CreateSwapchainKHR(createInfo);
		if (oldSwapchain != null)
			CleanupSwapchain();

		return (swapchain, format.Format, extent);
	}

	private ImageView[] CreateImageViews(Device device, Image[] swapImages)
	{
		var swapchainImageViews = new ImageView[swapImages.Length];

		for (int i = 0; i < swapchainImageViews.Length; i++)
		{
			var createInfo = new ImageViewCreateInfo
			{
				Image = swapImages[i],
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

			swapchainImageViews[i] = device.CreateImageView(createInfo);
		}

		return swapchainImageViews;
	}

	#endregion Swap chain

	private Instance CreateInstance()
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

		return new Instance(createInfo);
	}

	private void MainLoop()
	{
		var start = DateTime.Now;
		while (!_window.ShouldClose)
		{
			try
			{
				_window.PollEvents();
				if (_needResize)
				{
					_device!.WaitIdle();
					RecreateSwapchain(_device!, _physicalDevice!, _surface!, _renderPass!);
					_needResize = false;
					continue;
				}
				var delta = (DateTime.Now - start);
				var deltaS = delta.TotalSeconds;
				DrawFrame(deltaS);
				start = DateTime.Now;
				Thread.Sleep(60);
			}
			catch (ResultException ex)
			{
				Console.WriteLine($"Fatal Error: {ex.Result}");
				break;
			}
		}
		_device!.WaitIdle();
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

	private static void SetupDebugMessenger(Instance instance)
	{
		var debugCallback = new DebugReportCallback(DebugReportCallback);
		instance!.EnableDebug(debugCallback);
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

	private void CleanupSwapchain()
	{
		Console.WriteLine("Cleaning up swapchain");
		for (int i = 0; i < _framebuffers.Length; i++)
			_device?.DestroyFramebuffer(_framebuffers[i]);
		for (int i = 0; i < _swapchainImageViews.Length; i++)
			_device?.DestroyImageView(_swapchainImageViews[i]);
		if (_swapchain != null)
			_device?.DestroySwapchainKHR(_swapchain);
	}

	private void Cleanup()
	{
		if (_cleaning)
			return;
		_cleaning = true;
		_device?.WaitIdle();
		foreach (var sema in _imageAvailableSemaphores)
			_device?.DestroySemaphore(sema);
		foreach (var sema in _renderFinishedSemaphores)
			_device?.DestroySemaphore(sema);
		foreach (var fence in _inFlightFences)
			_device?.DestroyFence(fence);
		if (_commandPool != null)
			_device?.DestroyCommandPool(_commandPool);

		foreach (var shader in _shaderModules)
			_device?.DestroyShaderModule(shader);

		CleanupSwapchain();

		if (_graphicsPipeline != null)
			_device?.DestroyPipeline(_graphicsPipeline);
		if (_pipelineLayout != null)
			_device?.DestroyPipelineLayout(_pipelineLayout);
		if (_renderPass != null)
			_device?.DestroyRenderPass(_renderPass);

		if (_surface != null)
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

	//~MeteoraApp()
	//{
	//	// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
	//	Dispose(disposing: false);
	//}

	public void Dispose()
	{
		// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	#endregion Cleaup
}