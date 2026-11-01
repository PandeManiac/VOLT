using System.ComponentModel;
using System.Diagnostics;

namespace Volt.Editor;

/// <summary>
/// Hands a project file to the application the operating system
/// associates with its extension.
/// </summary>
/// <remarks>
/// The editor has no built-in code editor, so game code opens in whatever
/// the user already uses for C#. A missing association is an ordinary
/// outcome on a fresh machine, so failure is reported rather than thrown.
/// </remarks>
internal static class ExternalFileLauncher
{
	public static bool Open(string fullPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

		try
		{
			Process.Start(new ProcessStartInfo(fullPath)
			{
				UseShellExecute = true
			})?.Dispose();

			return true;
		}

		catch (Exception exception)
			when (exception is InvalidOperationException or
				Win32Exception or
				ObjectDisposedException)
		{
			return false;
		}
	}
}
