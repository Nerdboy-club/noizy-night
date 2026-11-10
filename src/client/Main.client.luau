-- Client entry point: input, HUD, shop panel and local effects only. All game rules live on the server.

local Controllers = script.Parent:WaitForChild("Controllers")

for _, name in ipairs({ "InputController", "HudController", "ShopController" }) do
	local ok, message = xpcall(function()
		require(Controllers:WaitForChild(name)).Start()
	end, debug.traceback)
	if not ok then
		warn(string.format("[SilentNight] %s failed to start:\n%s", name, tostring(message)))
	end
end
