# DeviceGlance

Always-visible desktop widget (plus tray icon) showing battery, connection state and last-seen time for every
paired Bluetooth device. Low-battery notification at <= 20% (re-arms above 30%).

- Build/test: `dotnet test`; publish: `dotnet publish src/DeviceGlance -c Release -r win-x64 --self-contained false -o publish`
  (needs the .NET 10 desktop + ASP.NET Core runtimes).
- Data lives in `%LOCALAPPDATA%\DeviceGlance` (`settings.json`, `state.json`, `log.txt`).
- Devices without Bluetooth battery data (iPad) report over Wi-Fi: tray menu -> "iPad setup..." shows the URL,
  token and Shortcuts steps. `POST /api/report` `{"device","battery","charging"?}` and `GET /api/devices`, both
  requiring header `X-DeviceGlance-Token`.
- Inbound firewall rule (elevated, once): `netsh advfirewall firewall add rule name="DeviceGlance" dir=in action=allow protocol=TCP localport=8765 profile=private`
