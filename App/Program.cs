using Meteora;
using Meteora.Window;

Console.WriteLine("Hello, World!");
using var app = new MeteoraApp(new GlfwWindow(1280, 720, "Hello world"));
app.Run();