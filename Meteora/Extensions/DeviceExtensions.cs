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
		var indices = new QueueFamilyIndices();

		for (int i = 0; i < props.Length; i++)
		{
			var fam = props[i];
			if (fam.QueueFlags.HasFlag(QueueFlags.Graphics))
				indices.graphics = (uint)i;

			if (device!.GetSurfaceSupportKHR((uint)i, surface))
				indices.presentation = (uint)i;

			if (indices.IsComplete)
				break;
		}

		return indices;
	}


	public static bool IsDeviceSuitable(this PhysicalDevice device, SurfaceKhr surface, string[] extensions)
	{
		var fams = device.FindQueueFamilies(surface);

		return fams.IsComplete && device.AreExtensionsSupported(extensions) && IsSwapChainAdequate(device, surface);
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
}
