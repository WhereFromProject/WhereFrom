using System.Text;
using WhereFrom.Cli;

Console.OutputEncoding = new UTF8Encoding(false);
return CommandLine.Run(args, Console.Out, Console.Error);
