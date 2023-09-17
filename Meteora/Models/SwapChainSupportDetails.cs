using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Vulkan;

namespace Meteora.Models;
public struct SwapChainSupportDetails
{
	public SurfaceCapabilitiesKhr capabilities;
	public SurfaceFormatKhr[] formats;
	public PresentModeKhr[] presentModes;

	public readonly SurfaceFormatKhr ChooseSwapSurfaceFormat()
	{
		return formats.FirstOrDefault(f => f is { Format: Format.B8G8R8A8Srgb, ColorSpace: ColorSpaceKhr.SrgbNonlinear }, formats.First());
	}

	public readonly PresentModeKhr ChooseSwapPresentMode()
	{
		return presentModes.FirstOrDefault(m => m == PresentModeKhr.Mailbox, PresentModeKhr.Fifo);
	}

	public readonly Extent2D ChooseSwapExtent(uint width, uint height)
	{
		if(capabilities.CurrentExtent.Width != uint.MaxValue)
			return capabilities.CurrentExtent;
		else
		{
			var ext = new Extent2D
			{
				Width = width,
				Height = height
			};

			ext.Width = Math.Clamp(ext.Width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width);
			ext.Height = Math.Clamp(ext.Height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height);

			return ext;
		}

	}
}
