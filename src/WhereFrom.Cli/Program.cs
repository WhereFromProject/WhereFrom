using System.Reflection;

var version = Assembly.GetExecutingAssembly().GetName().Version;
Console.WriteLine($"WhereFrom {version?.ToString(3)}");
