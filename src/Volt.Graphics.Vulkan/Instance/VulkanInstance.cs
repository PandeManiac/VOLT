
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Volt.Graphics.Vulkan.Instance;

/// <summary>
/// Owns a Vulkan instance and its Vulkan API loader.
/// </summary>
/// <remarks>
/// Native handles obtained from this instance are borrowed.
/// All dependent Vulkan resources must be destroyed before this instance.
/// </remarks>
public sealed unsafe class VulkanInstance : IDisposable
{
	private const string ValidationLayerName = "VK_LAYER_KHRONOS_validation";

	private const string EngineName = "VOLT";
	private const int MaxEnumerationAttempts = 4;

	private readonly Vk _api;
	private readonly HashSet<string> _enabledExtensions;

	private Silk.NET.Vulkan.Instance _handle;
	private bool _disposed;

	public VulkanInstanceDescription Description { get; }

	public uint LoaderApiVersion { get; }
	public uint RequestedApiVersion { get; }

	public bool ValidationEnabled => Description.EnableValidation;

	/// <summary>
	/// Gets the Vulkan API binding owned by this instance.
	/// </summary>
	public Vk Api
	{
		get
		{
			ThrowIfDisposed();
			return _api;
		}
	}

	/// <summary>
	/// Gets the borrowed native Vulkan instance handle.
	/// </summary>
	public Silk.NET.Vulkan.Instance Handle
	{
		get
		{
			ThrowIfDisposed();
			return _handle;
		}
	}

	public VulkanInstance(
		VulkanInstanceDescription description,
		IReadOnlyList<string>? requiredExtensions = null)
	{
		description.Validate();

		string[] extensions = requiredExtensions?.ToArray() ?? [];
		ValidateExtensions(extensions);

		_enabledExtensions = new HashSet<string>(
			extensions,
			StringComparer.Ordinal);

		Description = description;

		RequestedApiVersion =
			(description.ApiMajor << 22) |
			(description.ApiMinor << 12);

		_api = Vk.GetApi();

		try
		{
			LoaderApiVersion = QueryLoaderVersion(_api);

			if ((LoaderApiVersion & 0xFFFFF000u) < RequestedApiVersion)
			{
				throw new NotSupportedException(
					$"Vulkan {description.ApiMajor}.{description.ApiMinor} " +
					"was requested, but the loader only supports " +
					$"{FormatVersion(LoaderApiVersion)}.");
			}

			if (description.EnableValidation)
			{
				VerifyValidationLayer(_api);
			}

			if (extensions.Length > 0)
			{
				VerifyExtensions(_api, extensions);
			}

			_handle = CreateInstance(_api, description, extensions);
		}

		catch
		{
			if (_handle.Handle != 0)
			{
				_api.DestroyInstance(_handle, null);
			}

			_api.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Checks whether the Vulkan loader advertises an instance extension.
	/// </summary>
	/// <remarks>
	/// This does not enable the extension. It can be called before
	/// creating a VulkanInstance.
	/// </remarks>
	public static bool IsExtensionSupported(string extensionName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(extensionName);

		using Vk api = Vk.GetApi();

		for (int attempt = 0;
			 attempt < MaxEnumerationAttempts;
			 attempt++)
		{
			uint count = 0;

			EnsureSuccess(
				api.EnumerateInstanceExtensionProperties(
					(byte*)null,
					ref count,
					null),
				"Count Vulkan instance extensions");

			if (count == 0)
			{
				return false;
			}

			ExtensionProperties[] properties =
				new ExtensionProperties[checked((int)count)];

			fixed (ExtensionProperties* pointer = properties)
			{
				Result result =
					api.EnumerateInstanceExtensionProperties(
						(byte*)null,
						ref count,
						pointer);

				if (result == Result.Incomplete ||
					count > (uint)properties.Length)
				{
					continue;
				}

				EnsureSuccess(
					result,
					"Enumerate Vulkan instance extensions");

				for (int index = 0;
					 index < checked((int)count);
					 index++)
				{
					string? name = Marshal.PtrToStringUTF8(
						(nint)pointer[index].ExtensionName);

					if (name == extensionName)
					{
						return true;
					}
				}
			}

			return false;
		}

		throw new InvalidOperationException(
			"The Vulkan instance extension list kept changing.");
	}

	/// <summary>
	/// Gets whether this instance was created with the specified
	/// extension explicitly enabled.
	/// </summary>
	public bool IsExtensionEnabled(string extensionName)
	{
		ThrowIfDisposed();
		ArgumentException.ThrowIfNullOrWhiteSpace(extensionName);
		return _enabledExtensions.Contains(extensionName);
	}

	/// <summary>
	/// Enumerates native physical-device handles associated with this instance.
	/// The returned handles remain valid only while this instance is alive.
	/// </summary>
	public PhysicalDevice[] EnumeratePhysicalDevices()
	{
		ThrowIfDisposed();

		for (int attempt = 0; attempt < MaxEnumerationAttempts; attempt++)
		{
			uint count = 0;

			EnsureSuccess(
				_api.EnumeratePhysicalDevices(_handle, ref count, null),
				"Count physical devices");

			if (count == 0)
			{
				return [];
			}

			PhysicalDevice[] devices = new PhysicalDevice[count];

			fixed (PhysicalDevice* pointer = devices)
			{
				Result result = _api.EnumeratePhysicalDevices(
					_handle,
					ref count,
					pointer);

				if (result == Result.Incomplete)
				{
					continue;
				}

				EnsureSuccess(result, "Enumerate physical devices");
			}

			if (count != devices.Length)
			{
				Array.Resize(ref devices, checked((int)count));
			}

			return devices;
		}

		throw new InvalidOperationException(
			"The Vulkan physical-device list kept changing during enumeration.");
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_api.DestroyInstance(_handle, null);
		_handle = default;

		_api.Dispose();
		_disposed = true;
	}

	private static uint QueryLoaderVersion(Vk api)
	{
		uint version = Vk.Version10;

		try
		{
			EnsureSuccess(
				api.EnumerateInstanceVersion(ref version),
				"Query Vulkan loader version");
		}

		catch (EntryPointNotFoundException)
		{
			return Vk.Version10;
		}

		return version;
	}

	private static Silk.NET.Vulkan.Instance CreateInstance(
		Vk api,
		VulkanInstanceDescription description,
		string[] extensions)
	{
		List<nint> allocations = [];

		try
		{
			byte* applicationName = AllocateUtf8(
				description.ApplicationName,
				allocations);

			byte* engineName = AllocateUtf8(
				EngineName,
				allocations);

			ApplicationInfo applicationInfo = new()
			{
				SType = StructureType.ApplicationInfo,
				PApplicationName = applicationName,
				ApplicationVersion = Vk.MakeVersion(1, 0, 0),
				PEngineName = engineName,
				EngineVersion = Vk.MakeVersion(1, 0, 0),

				ApiVersion =
					(description.ApiMajor << 22) |
					(description.ApiMinor << 12)
			};

			byte* validationLayerName = null;
			byte** validationLayers = null;

			if (description.EnableValidation)
			{
				validationLayerName = AllocateUtf8(
					ValidationLayerName,
					allocations);

				validationLayers = &validationLayerName;
			}

			byte** extensionNames = null;

			if (extensions.Length > 0)
			{
				nint* extensionPointers = stackalloc nint[extensions.Length];

				for (int index = 0; index < extensions.Length; index++)
				{
					extensionPointers[index] = (nint)AllocateUtf8(
						extensions[index],
						allocations);
				}

				extensionNames = (byte**)extensionPointers;
			}

			InstanceCreateInfo createInfo = new()
			{
				SType = StructureType.InstanceCreateInfo,
				PApplicationInfo = &applicationInfo,
				EnabledLayerCount =
					description.EnableValidation ? 1u : 0u,
				PpEnabledLayerNames = validationLayers,
				EnabledExtensionCount = (uint)extensions.Length,
				PpEnabledExtensionNames = extensionNames
			};

			Result result = api.CreateInstance(
				in createInfo,
				null,
				out Silk.NET.Vulkan.Instance instance);

			if (result != Result.Success)
			{
				throw new InvalidOperationException(
					$"Vulkan instance creation failed: {result}.");
			}

			if (instance.Handle == 0)
			{
				throw new InvalidOperationException(
					"Vulkan reported successful instance creation " +
					"but returned a null handle.");
			}

			return instance;
		}

		finally
		{
			foreach (nint allocation in allocations)
			{
				Marshal.FreeCoTaskMem(allocation);
			}
		}
	}

	private static void VerifyValidationLayer(Vk api)
	{
		for (int attempt = 0; attempt < MaxEnumerationAttempts; attempt++)
		{
			uint count = 0;

			EnsureSuccess(
				api.EnumerateInstanceLayerProperties(ref count, null),
				"Count Vulkan instance layers");

			if (count == 0)
			{
				break;
			}

			LayerProperties[] layers = new LayerProperties[count];

			fixed (LayerProperties* pointer = layers)
			{
				Result result = api.EnumerateInstanceLayerProperties(
					ref count,
					pointer);

				if (result == Result.Incomplete)
				{
					continue;
				}

				EnsureSuccess(result, "Enumerate Vulkan instance layers");

				for (int index = 0; index < count; index++)
				{
					string? name = Marshal.PtrToStringUTF8(
						(nint)pointer[index].LayerName);

					if (name == ValidationLayerName)
					{
						return;
					}
				}
			}

			break;
		}

		throw new NotSupportedException(
			$"Vulkan validation was requested, but " +
			$"'{ValidationLayerName}' is unavailable.");
	}

	private static void VerifyExtensions(
		Vk api,
		string[] requiredExtensions)
	{
		HashSet<string> available = new(StringComparer.Ordinal);
		bool completed = false;

		for (int attempt = 0; attempt < MaxEnumerationAttempts; attempt++)
		{
			uint count = 0;

			EnsureSuccess(
				api.EnumerateInstanceExtensionProperties(
					(byte*)null,
					ref count,
					null),
				"Count Vulkan instance extensions");

			if (count == 0)
			{
				completed = true;
				break;
			}

			ExtensionProperties[] properties =
				new ExtensionProperties[count];

			fixed (ExtensionProperties* pointer = properties)
			{
				Result result = api.EnumerateInstanceExtensionProperties(
					(byte*)null,
					ref count,
					pointer);

				if (result == Result.Incomplete)
				{
					continue;
				}

				EnsureSuccess(result, "Enumerate Vulkan instance extensions");

				for (int index = 0; index < count; index++)
				{
					string? name = Marshal.PtrToStringUTF8(
						(nint)pointer[index].ExtensionName);

					if (name is not null)
					{
						available.Add(name);
					}
				}
			}

			completed = true;
			break;
		}

		if (!completed)
		{
			throw new InvalidOperationException(
				"The Vulkan instance-extension list kept changing.");
		}

		foreach (string extension in requiredExtensions)
		{
			if (!available.Contains(extension))
			{
				throw new NotSupportedException(
					$"Required Vulkan instance extension " +
					$"'{extension}' is unavailable.");
			}
		}
	}

	private static void ValidateExtensions(string[] extensions)
	{
		HashSet<string> unique = new(StringComparer.Ordinal);

		foreach (string extension in extensions)
		{
			if (string.IsNullOrWhiteSpace(extension))
			{
				throw new ArgumentException(
					"Extension names cannot be null, empty, or whitespace.",
					nameof(extensions));
			}

			if (extension.Contains('\0'))
			{
				throw new ArgumentException(
					"Extension names cannot contain null characters.",
					nameof(extensions));
			}

			if (!unique.Add(extension))
			{
				throw new ArgumentException(
					$"Duplicate Vulkan instance extension: '{extension}'.",
					nameof(extensions));
			}
		}
	}

	private static byte* AllocateUtf8(
		string value,
		List<nint> allocations)
	{
		nint pointer = Marshal.StringToCoTaskMemUTF8(value);
		allocations.Add(pointer);
		return (byte*)pointer;
	}

	private static string FormatVersion(uint version)
	{
		uint major = (version >> 22) & 0x7Fu;
		uint minor = (version >> 12) & 0x3FFu;
		uint patch = version & 0xFFFu;

		return $"{major}.{minor}.{patch}";
	}

	private static void EnsureSuccess(Result result, string operation)
	{
		if (result != Result.Success)
		{
			throw new InvalidOperationException(
				$"{operation} failed: {result}.");
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
