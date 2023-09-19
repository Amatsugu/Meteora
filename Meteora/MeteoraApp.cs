#define ENABLE_VALIDATION_LAYERS

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
	private Image[]? _swapImages;
	private Format _curSwapChainFormat;
	private Extent2D _curSwapChainExtent;
	private ImageView[]? _swapchainImageViews;
	private PipelineLayout? _pipelineLayout;
	private RenderPass? _renderPass;
	private Pipeline? _graphicsPipeline;
	private Framebuffer[]? _framebuffers;
	private CommandPool? _commandPool;
	private CommandBuffer? _commandBuffer;

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
#if ENABLE_VALIDATION_LAYERS
		SetupDebugMessenger(_instance);
#endif
		_surface = PrepareSurface(_instance);

		//Device
		_physicalDevice = PickPhysicalDevice(_instance, _surface);
		var indexFamilies = _physicalDevice.FindQueueFamilies(_surface);
		_device = CreateLogicalDevice(_physicalDevice, _surface, indexFamilies);
		_graphicsQueue = _device.GetQueue((uint)indexFamilies.graphics!, 0);
		_presentQueue = _device.GetQueue((uint)indexFamilies.presentation!, 0);

		//Swapchain
		(_swapchain, _curSwapChainFormat, _curSwapChainExtent) = CreateSwapChain(_physicalDevice, _surface, _device);
		_swapImages = _device.GetSwapchainImagesKHR(_swapchain);
		_swapchainImageViews = CreateImageViews(_device, _swapImages);
		
		//Graphics Pipeline
		_renderPass = CreateRenderPass(_device, _curSwapChainFormat);
		(_pipelineLayout, _graphicsPipeline) = CreateGraphicsPipeline(_device, _curSwapChainExtent, _renderPass);

		//Drawing
		_framebuffers = CreateFrameBuffers(_device, _swapchainImageViews, _renderPass, _curSwapChainExtent);
		_commandPool = CreateCommandPool(_device, indexFamilies);
		_commandBuffer = CreateCommandBuffer(_device, _commandPool);
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

	#region Drawing

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

	private static CommandBuffer CreateCommandBuffer(Device device, CommandPool commandPool)
	{
		var allocInfo = new CommandBufferAllocateInfo
		{
			CommandPool = commandPool,
			CommandBufferCount = 1,
			Level = CommandBufferLevel.Primary
		};

		return device.AllocateCommandBuffers(allocInfo).First();
	}

	private void RecordCommandBuffer(CommandBuffer commandBuffer, Framebuffer framebuffer, RenderPass renderPass, Extent2D extent, Pipeline graphicsPipeline)
	{
		var beginInfo = new CommandBufferBeginInfo
		{
		};

		commandBuffer.Begin(beginInfo);

		var clearvalue = new ClearValue
		{
			Color = new ClearColorValue
			{
				Float32 = new[] { 0f, 0f, 0f, 1f }
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

	private void DrawFrame()
	{

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
			Subpasses = new[] { subpass }
		};

		return device.CreateRenderPass(renderPassCreateInfo);
	}

	private (PipelineLayout pipelineLayout, Pipeline graphicsPipeline) CreateGraphicsPipeline(Device device, Extent2D extent, RenderPass renderPass)
	{
		var vertModule = device.CreateShaderModule(File.ReadAllBytes("Shaders/spv/vert.spv"));
		var fragModule = device.CreateShaderModule(File.ReadAllBytes("Shaders/spv/frag.spv"));

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

		return (pipelineLayout, graphicsPipeline.First());
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

	private (SwapchainKhr swapchain, Format format, Extent2D extent) CreateSwapChain(PhysicalDevice physicalDevice, SurfaceKhr surface, Device device)
	{
		var swapSupport = physicalDevice.QuerySwapChainSupport(surface);

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

		var swapchain = device.CreateSwapchainKHR(createInfo);


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
		while (!_window.ShouldClose)
		{
			_window.PollEvents();
			_window.Title = "test";
			DrawFrame();
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

	private void Cleanup()
	{
		if (_commandPool != null)
			_device?.DestroyCommandPool(_commandPool);
		if (_framebuffers != null)
		{
			for (int i = 0; i < _framebuffers.Length; i++)
				_device?.DestroyFramebuffer(_framebuffers[i]);
		}
		if (_graphicsPipeline != null)
			_device?.DestroyPipeline(_graphicsPipeline);
		if (_pipelineLayout != null)
			_device?.DestroyPipelineLayout(_pipelineLayout);
		if (_renderPass != null)
			_device?.DestroyRenderPass(_renderPass);
		if (_swapchainImageViews != null)
		{
			for (int i = 0; i < _swapchainImageViews.Length; i++)
				_device?.DestroyImageView(_swapchainImageViews[i]);
		}
		if (_swapchain != null)
			_device?.DestroySwapchainKHR(_swapchain);
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