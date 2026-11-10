-- Server entry point: builds the world, then starts every service in dependency order.
-- A service that fails to start is reported and skipped so the rest of the game keeps running.

local Services = script.Parent.Services
local World = script.Parent.World

local Build = require(World.Build)

Build.applyLighting()

local world = {
	Lobby = require(World.LobbyBuilder).Build(),
	Hub = require(World.HubBuilder).Build(),
	Rooftop = require(World.RooftopBuilder).Build(),
	House = require(World.HouseBuilder).Build(),
}

local ElfService = require(Services.ElfService)
local MissionService = require(Services.MissionService)

-- Elves ask the mission where they belong every time they (re)spawn.
ElfService.SetSpawnResolver(MissionService.GetSpawnCFrame)

local ordered = {
	"NoiseService",
	"ProfileService",
	"MonetizationService",
	"SkinService",
	"ElfService",
	"GiftService",
	"GrandpaService",
	"CatService",
	"DoorService",
	"MissionService",
	"HubService",
	"RooftopService",
	"LobbyService",
}

local failed = 0
for _, name in ipairs(ordered) do
	local ok, message = xpcall(function()
		require(Services[name]).Start(world)
	end, debug.traceback)
	if not ok then
		failed += 1
		warn(string.format("[SilentNight] %s failed to start:\n%s", name, tostring(message)))
	end
end

if failed == 0 then
	print("[SilentNight] Server started: world built, all services running.")
else
	warn(string.format("[SilentNight] Server started with %d failed service(s). See warnings above.", failed))
end
