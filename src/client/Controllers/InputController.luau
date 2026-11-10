-- Sends input intent to the server and applies the two things only the owning client can do:
-- knockback velocity when the server knocks our elf down, and jetpack thrust while the server
-- says the jetpack is burning (attribute "JetActive").
--
--   Shift (hold)  sprint          C  toggle sneak
--   Q (hold)      jetpack         R  sack: stash / take out
--   F             throw catnip    E  interact (ProximityPrompts)

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")
local UserInputService = game:GetService("UserInputService")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared:WaitForChild("Config"))
local Remotes = require(Shared:WaitForChild("Remotes"))

local SPRINT_KEYS = {
	[Enum.KeyCode.LeftShift] = true,
	[Enum.KeyCode.RightShift] = true,
	[Enum.KeyCode.ButtonL2] = true,
}
local CROUCH_KEYS = {
	[Enum.KeyCode.C] = true,
	[Enum.KeyCode.ButtonB] = true,
}
local JET_KEYS = {
	[Enum.KeyCode.Q] = true,
	[Enum.KeyCode.ButtonR2] = true,
}
local SACK_KEYS = {
	[Enum.KeyCode.R] = true,
	[Enum.KeyCode.ButtonY] = true,
}
local CATNIP_KEYS = {
	[Enum.KeyCode.F] = true,
	[Enum.KeyCode.ButtonL1] = true,
}

local InputController = {}

local wantSprint = false
local wantCrouch = false
local wantJet = false

local function sendMove()
	Remotes.MoveMode:FireServer(wantSprint, wantCrouch)
end

function InputController.Start()
	local player = Players.LocalPlayer

	UserInputService.InputBegan:Connect(function(input, gameProcessed)
		if gameProcessed then
			return
		end
		local key = input.KeyCode
		if SPRINT_KEYS[key] then
			wantSprint = true
			sendMove()
		elseif CROUCH_KEYS[key] then
			wantCrouch = not wantCrouch
			sendMove()
		elseif JET_KEYS[key] then
			wantJet = true
			Remotes.Jetpack:FireServer(true)
		elseif SACK_KEYS[key] then
			Remotes.SackToggle:FireServer()
		elseif CATNIP_KEYS[key] then
			Remotes.ThrowCatnip:FireServer()
		end
	end)

	UserInputService.InputEnded:Connect(function(input)
		local key = input.KeyCode
		if SPRINT_KEYS[key] and wantSprint then
			wantSprint = false
			sendMove()
		elseif JET_KEYS[key] and wantJet then
			wantJet = false
			Remotes.Jetpack:FireServer(false)
		end
	end)

	Remotes.Knockdown.OnClientEvent:Connect(function(velocity)
		local character = player.Character
		local root = character and character:FindFirstChild("HumanoidRootPart")
		if root and typeof(velocity) == "Vector3" and velocity.Magnitude > 0 then
			root.AssemblyLinearVelocity = velocity
		end
	end)

	-- Jetpack thrust: hold the upward speed while the server confirms fuel is burning.
	RunService.Heartbeat:Connect(function()
		if not wantJet or player:GetAttribute("JetActive") ~= true then
			return
		end
		local character = player.Character
		local root = character and character:FindFirstChild("HumanoidRootPart")
		if not root then
			return
		end
		local level = player:GetAttribute("Upg_Jetpack") or 0
		local thrust = (Config.Jetpack.BaseThrustMeters + Config.Jetpack.ThrustPerLevelMeters * level) * Config.StudsPerMeter
		local velocity = root.AssemblyLinearVelocity
		if velocity.Y < thrust then
			root.AssemblyLinearVelocity = Vector3.new(velocity.X, thrust, velocity.Z)
		end
	end)

	-- Re-send intent after respawn so the server state matches the keys.
	player.CharacterAdded:Connect(function()
		sendMove()
	end)
	sendMove()
end

return InputController
