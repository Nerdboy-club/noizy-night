-- Public lobby: a snowy square with queue pads (solo / duo / trio / squad) and a wardrobe shop.

local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Config = require(ReplicatedStorage:WaitForChild("Shared").Config)
local Build = require(script.Parent.Build)

local LobbyBuilder = {}

local ORIGIN = Vector3.new(0, 0, -600)
local FLOOR_TOP = ORIGIN.Y + 0.5
local HALF = 70

local PAD_COLORS = {
	Color3.fromRGB(90, 200, 120),
	Color3.fromRGB(90, 160, 240),
	Color3.fromRGB(240, 180, 70),
	Color3.fromRGB(230, 90, 90),
}

function LobbyBuilder.Build()
	local model = Instance.new("Model")
	model.Name = "Lobby"

	Build.floor(
		model,
		"Snow",
		Vector3.new(HALF * 2, 1, HALF * 2),
		ORIGIN,
		Color3.fromRGB(235, 240, 250),
		Enum.Material.Snow,
		"Carpet"
	)

	local fence = { BaseY = FLOOR_TOP, Height = 5, Color = Color3.fromRGB(120, 80, 56), Material = Enum.Material.Wood }
	Build.wall(model, "Fence", ORIGIN.X - HALF, ORIGIN.Z - HALF, ORIGIN.X + HALF, ORIGIN.Z - HALF, fence)
	Build.wall(model, "Fence", ORIGIN.X - HALF, ORIGIN.Z + HALF, ORIGIN.X + HALF, ORIGIN.Z + HALF, fence)
	Build.wall(model, "Fence", ORIGIN.X - HALF, ORIGIN.Z - HALF, ORIGIN.X - HALF, ORIGIN.Z + HALF, fence)
	Build.wall(model, "Fence", ORIGIN.X + HALF, ORIGIN.Z - HALF, ORIGIN.X + HALF, ORIGIN.Z + HALF, fence)

	-- Title board (its Front face looks towards -Z, where players spawn).
	local board = Build.block(
		model,
		"TitleBoard",
		Vector3.new(60, 14, 1),
		Vector3.new(ORIGIN.X, FLOOR_TOP + 16, ORIGIN.Z + 50),
		Color3.fromRGB(120, 30, 36),
		Enum.Material.Wood
	)
	Build.sign(
		board,
		Enum.NormalId.Front,
		"OPERATION: SILENT NIGHT\nВстаньте на площадку, чтобы начать смену",
		Color3.fromRGB(255, 240, 210)
	)

	-- Queue pads
	local pads = {}
	local count = #Config.Lobby.Pads
	for index, def in ipairs(Config.Lobby.Pads) do
		local x = ORIGIN.X + (index - (count + 1) / 2) * 26
		local part = Build.block(
			model,
			"Pad" .. def.Name,
			Vector3.new(18, 0.4, 18),
			Vector3.new(x, FLOOR_TOP + 0.2, ORIGIN.Z + 22),
			PAD_COLORS[index] or PAD_COLORS[1],
			Enum.Material.Neon
		)
		part.Transparency = 0.25
		local label = Build.billboard(part, def.Name, 9, 260, 110)
		table.insert(pads, { Part = part, Label = label, Name = def.Name, Size = def.Size })
	end

	-- Wardrobe / shop
	local wardrobe = Build.block(
		model,
		"Wardrobe",
		Vector3.new(10, 12, 4),
		Vector3.new(ORIGIN.X - 50, FLOOR_TOP + 6, ORIGIN.Z - 20),
		Color3.fromRGB(110, 70, 150),
		Enum.Material.Wood
	)
	Build.billboard(wardrobe, "Шкаф и магазин", 8, 260, 60)
	local shopPrompt = Build.prompt(wardrobe, "Шкаф эльфа", "Открыть магазин", 14)

	-- Decoration
	for _, offset in ipairs({ Vector3.new(-52, 0, 44), Vector3.new(52, 0, 44), Vector3.new(52, 0, -44) }) do
		Build.tree(model, Vector3.new(ORIGIN.X + offset.X, FLOOR_TOP, ORIGIN.Z + offset.Z))
	end
	for _, offset in ipairs({ Vector3.new(-30, 12, 0), Vector3.new(30, 12, 0), Vector3.new(0, 12, -40), Vector3.new(0, 14, 36) }) do
		Build.lamp(model, "Lantern", ORIGIN + offset, Color3.fromRGB(255, 214, 150), 2, 60)
	end

	model.Parent = workspace

	local spawnPosition = Vector3.new(ORIGIN.X, FLOOR_TOP + 3, ORIGIN.Z - 30)
	return {
		Model = model,
		SpawnCFrame = CFrame.lookAt(spawnPosition, spawnPosition + Vector3.new(0, 0, 10)),
		Pads = pads,
		ShopPrompt = shopPrompt,
	}
end

return LobbyBuilder
