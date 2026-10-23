namespace Volt.GameplayDemo;

internal static class Program
{
	private static int Main()
	{
		try
		{
			using ChargedDashDemo demo = new();

			demo.Run();

			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
	}
}
