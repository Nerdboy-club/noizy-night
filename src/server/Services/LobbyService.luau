-- Lobby queue pads. Stand on "Solo / Duo / Trio / Squad": a countdown starts as soon as someone is
-- on the pad and shortens when the pad is full. The party then gets its own reserved server
-- (published game) or starts the mission right here (Studio, or if the teleport fails).

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")
local TeleportService = game:GetService("TeleportService")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared.Config)
local Remotes = require(Shared.Remotes)

local MissionService = require(script.Parent.MissionService)

local LobbyConfig = Config.Lobby

local LobbyService = {}

local pads
local teleportingUntil = {}

local function toastAll(players, text)
	for _, player in ipairs(players) do
		if player.Parent then
			Remotes.Toast:FireClient(player, text)
		end
	end
end

local function isOnPad(pad, position)
	local point = pad.Part.CFrame:PointToObjectSpace(position)
	local half = pad.Part.Size / 2
	return math.abs(point.X) <= half.X and math.abs(point.Z) <= half.Z and point.Y > -1 and point.Y < 8
end

local function membersOf(pad, now)
	local members = {}
	for _, player in ipairs(Players:GetPlayers()) do
		if #members >= pad.Size then
			break
		end
		local busy = MissionService.IsCrew(player) or (teleportingUntil[player] or 0) > now
		local character = player.Character
		local root = character and character:FindFirstChild("HumanoidRootPart")
		if not busy and root and isOnPad(pad, root.Position) then
			table.insert(members, player)
		end
	end
	return members
end

local function startLocally(members)
	if not MissionService.Begin(members) then
		toastAll(members, "Смена уже идёт. Подождите, пока мастерская освободится.")
		return false
	end
	return true
end

local function launch(members, now)
	local useReserved = LobbyConfig.UseReservedServers and not RunService:IsStudio()
	if not useReserved then
		return startLocally(members)
	end

	for _, player in ipairs(members) do
		teleportingUntil[player] = now + 20
	end
	toastAll(members, "Запрягаем оленей…")

	local ok, message = pcall(function()
		local code = TeleportService:ReserveServer(game.PlaceId)
		TeleportService:TeleportToPrivateServer(game.PlaceId, code, members)
	end)
	if ok then
		return true
	end

	warn("[Lobby] reserved server teleport failed: " .. tostring(message))
	for _, player in ipairs(members) do
		teleportingUntil[player] = nil
	end
	return startLocally(members)
end

local function updatePad(pad, now)
	local members = membersOf(pad, now)
	local count = #members

	if count == 0 then
		pad.LaunchAt = nil
		pad.Label.Text = string.format("%s\n0/%d", pad.Name, pad.Size)
		return
	end

	if not pad.LaunchAt then
		pad.LaunchAt = now + LobbyConfig.Countdown
	end
	if count >= pad.Size then
		pad.LaunchAt = math.min(pad.LaunchAt, now + LobbyConfig.FullCountdown)
	end

	local remaining = pad.LaunchAt - now
	if remaining > 0 then
		pad.Label.Text = string.format("%s\n%d/%d · старт через %d", pad.Name, count, pad.Size, math.ceil(remaining))
		return
	end

	pad.Label.Text = pad.Name .. "\nПоехали!"
	local started = launch(members, now)
	-- Give a failed start a full countdown before it retries.
	pad.LaunchAt = started and nil or (os.clock() + LobbyConfig.Countdown)
end

function LobbyService.Start(world)
	local lobby = world.Lobby
	pads = lobby.Pads

	lobby.ShopPrompt.Triggered:Connect(function(player)
		Remotes.OpenWorkbench:FireClient(player)
	end)

	Players.PlayerRemoving:Connect(function(player)
		teleportingUntil[player] = nil
	end)

	-- A reserved mission server has no queue: everyone in it is already a crew.
	if MissionService.IsMissionServer then
		for _, pad in ipairs(pads) do
			pad.Label.Text = pad.Name
		end
		return
	end

	task.spawn(function()
		while true do
			task.wait(0.25)
			local now = os.clock()
			for _, pad in ipairs(pads) do
				local ok, message = pcall(updatePad, pad, now)
				if not ok then
					warn("[Lobby] pad update failed: " .. tostring(message))
				end
			end
		end
	end)
end

return LobbyService
