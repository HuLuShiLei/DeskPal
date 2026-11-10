# 模拟 Room 成员的 SignalR 小程序

放在 `.local/tmp/e2e/BobBot/`，用完和 `.local/tmp/e2e/` 一起删除。

## BobBot.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.14" />
    <ProjectReference Include="..\..\..\..\src\DeskPal.Shared\DeskPal.Shared.csproj" />
  </ItemGroup>
</Project>
```

## Program.cs

```csharp
using System.Text.Json.Serialization;
using DeskPal.Shared.Messaging;
using DeskPal.Shared.Presence;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var (server, token, roomId, seconds) = (args[0], args[1], Guid.Parse(args[2]), int.Parse(args[3]));
var hub = new HubConnectionBuilder()
    .WithUrl(server + HubContract.Path, o => o.AccessTokenProvider = () => Task.FromResult<string?>(token))
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .Build();
hub.On<Envelope>(HubContract.Receive, e =>
    Console.WriteLine($"{DateTime.Now:HH:mm:ss} RECV {e.Type} sender={e.SenderId} payload={e.Payload}"));
await hub.StartAsync();

var video = new PresenceSnapshot
{
    State = PresenceState.Slacking,
    Category = ActivityCategory.Video,
    AppDisplayName = "哔哩哔哩",
    Detail = "【猫猫】合集",
    Since = DateTimeOffset.UtcNow.AddMinutes(-12),
};
await hub.InvokeAsync(HubContract.Publish, Envelope.Create(MessageTypes.PresenceUpdate, roomId, video));
Console.WriteLine($"{DateTime.Now:HH:mm:ss} published");
await Task.Delay(TimeSpan.FromSeconds(seconds));
await hub.DisposeAsync();
```

`AddJsonProtocol` 是 `Microsoft.Extensions.DependencyInjection` 命名空间下的扩展方法，漏了这个 using 会编译失败。

要测其他消息，改 `Envelope.Create` 的类型和 payload 即可，例如发 `MessageTypes.Poke` 加 `new PokePayload("测试", aliceId)`。