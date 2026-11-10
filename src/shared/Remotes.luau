-- Single place that owns every RemoteEvent.
-- The server creates them; clients wait for them.
--
-- MoveMode      client -> server : (wantSprint: boolean, wantCrouch: boolean)
-- Jetpack       client -> server : (wantThrust: boolean)
-- SackToggle    client -> server : ()  stash the held gift / take the last one out
-- ThrowCatnip   client -> server : ()
-- BuyUpgrade    client -> server : (item: string)  an upgrade key or "Catnip"
-- Knockdown     server -> client : (velocity: Vector3) the elf just fell over
-- Toast         server -> client : (text: string)
-- RoundResult   server -> client : (result: table)
-- OpenWorkbench server -> client : ()

local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")

local NAMES = {
	"MoveMode",
	"Jetpack",
	"SackToggle",
	"ThrowCatnip",
	"BuyUpgrade",
	"BuySkin", -- client -> server : (skinId: string) buy with tips
	"EquipSkin", -- client -> server : (skinId: string)
	"PromptPurchase", -- client -> server : (kind: "Product" | "GamePass", key: string) opens the Robux prompt
	"Knockdown",
	"Toast",
	"RoundResult",
	"OpenWorkbench",
}
local FOLDER_NAME = "Remotes"

local Remotes = {}

if RunService:IsServer() then
	local folder = Instance.new("Folder")
	folder.Name = FOLDER_NAME
	for _, name in ipairs(NAMES) do
		local remote = Instance.new("RemoteEvent")
		remote.Name = name
		remote.Parent = folder
		Remotes[name] = remote
	end
	folder.Parent = ReplicatedStorage
else
	local folder = ReplicatedStorage:WaitForChild(FOLDER_NAME)
	for _, name in ipairs(NAMES) do
		Remotes[name] = folder:WaitForChild(name)
	end
end

return Remotes
