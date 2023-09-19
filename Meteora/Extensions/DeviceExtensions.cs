using Meteora.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Vulkan;

namespace Meteora.Extensions;
public static class DeviceExtensions
{

	public static bool AreExtensionsSupported(this PhysicalDevice device, string[] extensions)
	{
		var props = device.EnumerateDeviceExtensionProperties();

		return extensions.All(ext => props.Any(p => p.ExtensionName == ext));
	}

	public static QueueFamilyIndices FindQueueFamilies(this PhysicalDevice device, SurfaceKhr surface)
	{
		var props = device.GetQueueFamilyProperties();
		uint? graphics = null, presentation = null;

		for (int i = 0; i < props.Length; i++)
		{
			var fam = props[i];
			if (fam.QueueFlags.HasFlag(QueueFlags.Graphics))
				graphics = (uint)i;

			if (device!.GetSurfaceSupportKHR((uint)i, surface))
				presentation = (uint)i;

			if (graphics != null && presentation != null)
				break;
		}

		if(graphics != null && presentation != null)
			return new QueueFamilyIndices((uint) graphics, (uint)presentation);

		return default;
	}


	public static bool IsDeviceSuitable(this PhysicalDevice device, SurfaceKhr surface, string[] extensions)
	{
		var fams = device.FindQueueFamilies(surface);

		return fams.isComplete && device.AreExtensionsSupported(extensions) && IsSwapChainAdequate(device, surface);
	}

	public static bool IsSwapChainAdequate(this PhysicalDevice device, SurfaceKhr surface)
	{
		var details = QuerySwapChainSupport(device, surface);
		return details.formats.Any() && details.formats.Any();
	}

	public static SwapChainSupportDetails QuerySwapChainSupport(this PhysicalDevice device, SurfaceKhr surface)
	{
		var details = new SwapChainSupportDetails
		{
			capabilities = device.GetSurfaceCapabilitiesKHR(surface),
			formats = device.GetSurfaceFormatsKHR(surface),
			presentModes = device.GetSurfacePresentModesKHR(surface)
		};

		return details;
	}

	public static ShaderModule CreateShaderModule(this Device device, Stream code)
	{
		var codeBytes = new byte[code.Length];
		code.Read(codeBytes, 0, codeBytes.Length);
		var info = new ShaderModuleCreateInfo 
		{ 
			CodeBytes = codeBytes,
			CodeSize = (uint)codeBytes.Length,
		};
		return device.CreateShaderModule(info);
	}

	public static ShaderModule CreateShaderModule(this Device device, byte[] codeBytes)
	{
		var info = new ShaderModuleCreateInfo
		{
			CodeBytes = codeBytes,
			CodeSize = (uint)codeBytes.Length,
		};
		return device.CreateShaderModule(info);
	}
}
