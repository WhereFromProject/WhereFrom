using WhereFrom.NativeHost;

var origin = args.FirstOrDefault(argument => argument.StartsWith("chrome-extension://", StringComparison.Ordinal));
using var input = Console.OpenStandardInput();
using var output = Console.OpenStandardOutput();
var host = new NativeMessageHost();
host.Run(input, output, origin);
